using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TShockAPI;
using TerrariaApi.Server;
using Terraria;
using System.Net.Sockets;
using System.Net;
using System.IO;

namespace Tgo_Requests
{
    [ApiVersion(2, 1)]
    public class Tgo_Requests : TerrariaPlugin
    {
        private static int PORT = 10337;
        private static IPAddress IP = IPAddress.Parse("173.236.15.24");
        private static IPEndPoint IPE = new IPEndPoint(IP, PORT);

        public override string Name => "Tgo_Requests";

        public override Version Version => new Version(1,0);

        public override string Author => "Tsohg";

        public override string Description => "Used to handle requests from TGO GUIs.";

        public Tgo_Requests(Main game) : base(game)
        {
        }

        public override void Initialize()
        {

        }

        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {

            }
            base.Dispose(disposing);
        }

        private void Connect()
        {
            TcpListener listener = new TcpListener(IPE);
            //System.IO.File.AppendAllText(tgoLogPath, "Attempting to connect a client...");
            //sw.WriteLine("Attempting to connect a client...");
            try
            {
                while (true)
                {
                    listener.Start();
                    Socket client = listener.AcceptSocket();
                    if (client != null)
                    {
                        //sw.WriteLine("Client Connected: " + client.LocalEndPoint);
                        //System.IO.File.AppendAllText(tgoLogPath, "Client Connected: " + client.LocalEndPoint);
                        Tgo_Client_Listener tcl = new Tgo_Client_Listener(client);
                    }
                }
            }
            catch (Exception e)
            {
                listener.Stop();
                //System.IO.File.AppendAllText(tgoLogPath, "Error: " + e.Message);
                //sw.WriteLine("Error: " + e.Message);
            }
        }

        private class Tgo_Client_Listener
        {
            private Socket client;

            public Tgo_Client_Listener(Socket client)
            {
                this.client = client;
                ListenAsync();
            }

            public async void ListenAsync()
            {
                try
                {
                    while (client.Connected)
                    {
                        NetworkStream netStream = new NetworkStream(client);
                        StreamReader sr = new StreamReader(netStream);

                        //read message sent by client as a request...
                        string request = await sr.ReadToEndAsync();

                        //process request
                        Tgo_Req_Method req = new Tgo_Req_Method(request);
                    }
                }
                catch (Exception e)
                {

                }
            }
        }

        private class Tgo_Req_Method
        {
            public Tgo_Req_Method(string request)
            {
                string[] req = request.Split(',');
                if (req.Length < 2) //drop request if not in correct format.
                    return;
                TSPlayer tplr = GetTSPlayerByName(req[0]);

                //translate string to method. execute method if user has permissions.
                switch (req[1])
                {
                    case "HelloWorld":
                        if (tplr.HasPermission("TGO.HelloWorld"))
                            TShock.Log.ConsoleInfo("Hello World!");
                        else goto default;
                        break;
                    default:
                        TShock.Log.ConsoleError("" + tplr.Name + " tried to execute " +
                            req[1] + " but did not have permission!");
                        break;
                }
            }

            private TSPlayer GetTSPlayerByName(string name)
            {
                foreach (TSPlayer plr in TShock.Players)
                    if (plr.Name == name)
                        return plr;
                return null;
            }
        }
    }
}
