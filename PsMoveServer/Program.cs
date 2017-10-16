using System;
using System.Linq;
using System.Net;
using System.Diagnostics;
using System.Threading;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Text;
using Mono.Options;
using WebSocketSharp;
using Newtonsoft.Json;
using NetMQ;
using NetMQ.Sockets;

namespace PsMoveServer
{
    class PsMove
    {
        public float x;
        public float y;
        public float z;
        public int traingle;
        public int circle;
        public int square;
    }

    public class PayLoad
    {
        public string socketPath { get; set; }
        public bool returnValue { get; set; }
    }

    public class Response
    {
        public string type { get; set; }
        public int id { get; set; }
        public PayLoad payload { get; set; }
    }

    class Program
    {        
        static IPAddress validateIP(string ip)
        {
            if (ip.ToLower().Equals("localhost"))
            {
                return IPAddress.Loopback;
            }

            return IPAddress.Parse(ip);
        }
        static Int32 validatePort(Int32 port)
        {
            if (!Enumerable.Range(1, 65535).Contains(port)) throw new Exception("Invalid port number");
            return port;
        }
        static void Main(string[] args)
        {
            var verbosity = 0;
            IPAddress tvaddr = null;
            Int32 tvport = 3000;
            IPAddress controladdr = IPAddress.Loopback;
            Int32 controlport = 5556;

            var options = new OptionSet {
                 {"tvip=", "tv ip address" , i =>  tvaddr = validateIP(i) },
                 {"tvport=", (Int32 p) => tvport = validatePort(p) },
                 {"controlip=","Control ip", i => controladdr = validateIP(i)},
                 {"controlport=", (int p) => controlport = validatePort(p) },
                 { "v", "increase debug message verbosity", v => {
                        if (v != null)
                            ++verbosity;
                 } },
                };
            
            try
            {
                options.Parse(args);
                if (tvaddr == null) throw new Exception("Empty tv ip");

                Console.WriteLine(tvport);
                Console.WriteLine(tvaddr.ToString());
                Console.WriteLine(controlport);
                Console.WriteLine(controladdr.ToString());

                Run(tvaddr, tvport, controladdr, controlport);
            }
            catch (OptionException e)
            {
                Console.Write("greet: ");
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `greet --help' for more information.");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        static void Run(IPAddress tvaddr, Int32 tvport, IPAddress controladdr, Int32 controlport)
        {
            string response = string.Empty;
            bool res = false;
            Response resp = null;

            var tvws = new StringBuilder();
            tvws.Append("ws://");
            tvws.Append(tvaddr.ToString());
            tvws.Append(":");
            tvws.Append(tvport.ToString());
            Console.WriteLine(tvws.ToString());
            
            using (var ws = new WebSocket(tvws.ToString()))
            {
                try
                {
                    ws.OnMessage += (sender, e) =>
                    {
                        if (e.Data.Contains("netinput.pointer.sock"))
                        {
                            response = e.Data.ToString();
                            res = true;
                        }
                        else if (e.Data.Contains("client-key"))
                        {
                            ws.Send("{\"type\":\"request\",\"id\":5,\"uri\":\"ssap://com.webos.service.networkinput/getPointerInputSocket\"}");
                        }
                        else if (e.Data.Contains("denied"))
                        {
                            Console.WriteLine("Denied by user");
                            Process.GetCurrentProcess().Kill();
                        }
                    };

                    ws.Connect();

                    Task task = Task.Run(() => { ws.Connect(); });
                    if (!task.Wait(TimeSpan.FromSeconds(5)))
                    {
                        Console.WriteLine("TimeOut: Connect");
                        Process.GetCurrentProcess().Kill();
                    }

                    ws.Send("{\"type\":\"register\",\"id\":1,\"payload\":{\"manifest\":{\"manifestVersion\":1,\"permissions\":[\"LAUNCH\",\"LAUNCH_WEBAPP\",\"APP_TO_APP\",\"CONTROL_AUDIO\",\"CONTROL_INPUT_MEDIA_PLAYBACK\",\"CONTROL_POWER\",\"READ_INSTALLED_APPS\",\"CONTROL_DISPLAY\",\"CONTROL_INPUT_JOYSTICK\",\"CONTROL_INPUT_MEDIA_RECORDING\",\"CONTROL_INPUT_TV\",\"READ_INPUT_DEVICE_LIST\",\"READ_NETWORK_STATE\",\"READ_TV_CHANNEL_LIST\",\"WRITE_NOTIFICATION_TOAST\",\"CONTROL_INPUT_TEXT\",\"CONTROL_MOUSE_AND_KEYBOARD\",\"READ_CURRENT_CHANNEL\",\"READ_RUNNING_APPS\"]}}}");

                    var cancelToken = new CancellationTokenSource();
                    Task.Factory.StartNew(() => timeout(), cancelToken.Token);

                    while (ws.IsAlive && !res) ;
                    cancelToken.Cancel(false);
                    resp = JsonConvert.DeserializeObject<Response>(response);
                }

                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    Process.GetCurrentProcess().Kill();
                }
            }
           
            using (var ws = new WebSocket(resp.payload.socketPath))
            {
                ws.Connect();
                using (var subSocket = new SubscriberSocket())
                {
                    var controlzmq = new StringBuilder();
                    controlzmq.Append("tcp://");
                    controlzmq.Append(controladdr.ToString());
                    controlzmq.Append(":");
                    controlzmq.Append(controlport.ToString());

                    subSocket.Options.ReceiveHighWatermark = 1000;
                    subSocket.Connect(controlzmq.ToString());
                    subSocket.Subscribe("x");

                    float x = 0.0f;
                    float y = 0.0f;
                    float multi = 10.0f;
                    int square_flag = 0;
                    int traingle_flag = 0;

                    while (true)
                    {                        
                        string messageTopicReceived = subSocket.ReceiveFrameString();
                        Console.WriteLine(messageTopicReceived);
                        string messageReceived = subSocket.ReceiveFrameString();
                        Console.WriteLine(messageReceived);

                        PsMove psmove = JsonConvert.DeserializeObject<PsMove>(messageReceived);
                        var sb = new StringBuilder();

                        x = psmove.x;
                        y = psmove.y;

                        if (psmove.circle == 1)
                        {
                            sb = new StringBuilder();

                            sb.Append("type:button\n");
                            sb.Append("name:" + "LEFT" + "\n");
                            sb.Append("\n");

                            ws.Send(sb.ToString());
                            continue;
                        }

                        if (psmove.square == 1 && square_flag == 0)
                        {

                            square_flag = 1;
                            sb.Append("type:button\n");
                            sb.Append("name:HOME\n");
                            sb.Append("\n");
                            ws.Send(sb.ToString());
                        }

                        if (psmove.square == 0 && square_flag == 1) square_flag = 0;

                        if (psmove.traingle == 1 && traingle_flag == 0)
                        {

                            traingle_flag = 1;
                            sb.Append("type:click\n");
                            sb.Append("\n");
                            ws.Send(sb.ToString());
                        }

                        if (psmove.traingle == 0 && traingle_flag == 1) traingle_flag = 0;

                        sb.Append("type:move\n");
                        sb.Append("dx:" + psmove.z * multi * -1 + "\n");
                        sb.Append("dy:" + psmove.x * multi * -1 + "\n");
                        sb.Append("down:" + "0" + "\n");
                        sb.Append("\n");
                        ws.Send(sb.ToString());
                    }
                }

            }
        }

        static void Ping(IPAddress address)
        {
            Ping pingSender = new Ping();
            PingReply reply = pingSender.Send(address);
            if (!reply.Status.Equals(IPStatus.Success))
                throw new Exception("Ping error");
        }
        public static async Task timeout()
        {

            //await Task.Delay(TimeSpan.FromSeconds(10));
            //Console.WriteLine("TimeOut:User Response");
            //Process.GetCurrentProcess().Kill();
        }
    }
}
