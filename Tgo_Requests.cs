using System;
using System.Collections.Generic;
using TShockAPI;
using TerrariaApi.Server;
using Terraria;
using System.Net.Sockets;
using System.Net;
using System.IO;
using System.Threading;

namespace Tgo_Requests
{
    [ApiVersion(2, 1)]
    public class Tgo_Requests : TerrariaPlugin
    {
        private static readonly int PORT = 10337;
        private static readonly IPAddress IP = IPAddress.Parse("173.236.15.24");
        private static readonly IPEndPoint IPE = new IPEndPoint(IP, PORT);
        private static Thread t;
        private List<Socket> tempClients; //temporary client buffer
        public static Dictionary<Socket, Tuple<TSPlayer, List<ushort>>> tgoUsers; //mapping of socket to a TSPlayer and their world edit clipboard.

        public override string Name => "Tgo_Requests";

        public override Version Version => new Version(1, 0);

        public override string Author => "Tsohg";

        public override string Description => "Used to handle requests from TGO GUIs.";

        public Tgo_Requests(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            //TShock.Log.ConsoleInfo("Hello world");
            tempClients = new List<Socket>();
            tgoUsers = new Dictionary<Socket, Tuple<TSPlayer, List<ushort>>>();
            TShockAPI.Hooks.PlayerHooks.PlayerPostLogin += LinkTgo;
            TShockAPI.Hooks.PlayerHooks.PlayerLogout += UnlinkTgo;
            t = new Thread(new ThreadStart(Connect));
            t.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                t.Abort();
                TShockAPI.Hooks.PlayerHooks.PlayerPostLogin -= LinkTgo;
                TShockAPI.Hooks.PlayerHooks.PlayerLogout -= UnlinkTgo;
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Links client socket remote ip in list to TSPlayer's ip address.
        /// TODO: Set up a dictionary with the socket being the key, and TSPlayer Name being the value.
        /// TODO: On Player Logout, remove the socket/name entry from the dictionary.
        /// </summary>
        /// <param name="args"></param>
        private void LinkTgo(TShockAPI.Hooks.PlayerPostLoginEventArgs args)
        {
            foreach (Socket c in tempClients)
            {
                //user.key = socket
                TShock.Log.ConsoleInfo(c.RemoteEndPoint.ToString().Split(':')[0] + "     " + args.Player.IP);
                if (c.RemoteEndPoint.ToString().Split(':')[0] == args.Player.IP) //will require more testing than just this.
                {
                    //Send signal to TgoExt which is the TSPlayer name.
                    StreamWriter sw = new StreamWriter(new NetworkStream(c));
                    sw.WriteLine(args.Player.Name);
                    sw.Flush();
                    //Set up client listener.
                    Tgo_Client_Listener tgcl = new Tgo_Client_Listener(c);
                    //map socket to player and remove from temp. client storage
                    tgoUsers.Add(c, new Tuple<TSPlayer, List<ushort>>(args.Player, new List<ushort>()));
                    tempClients.Remove(c);
                }
            }
        }

        private void UnlinkTgo(TShockAPI.Hooks.PlayerLogoutEventArgs args)
        {
            foreach (KeyValuePair<Socket, Tuple<TSPlayer, List<ushort>>> tgoUser in tgoUsers)
            {
                if (tgoUser.Value.Item1.Name == args.Player.Name)
                {
                    tgoUsers.Remove(tgoUser.Key);
                    break;
                }
            }
        }

        private void Connect()
        {
            TcpListener listener = new TcpListener(IPE);
            listener.Start();
            try
            {
                while (true)
                {
                    Socket client = listener.AcceptSocket();
                    if (client != null)
                    {
                        TShock.Log.ConsoleInfo("TGO: Connected a client => " + client.RemoteEndPoint.ToString());
                        tempClients.Add(client);
                    }
                }
            }
            catch (Exception e)
            {
                listener.Stop();
                TShock.Log.ConsoleInfo("TGO Error: " + e.Message);
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
                        //TShock.Log.ConsoleInfo("Attempting to recieve a message...");
                        string request = await sr.ReadLineAsync();
                        //TShock.Log.ConsoleInfo("Request Recieved: " + request);
                        //process request
                        Tgo_Req_Method req = new Tgo_Req_Method(request, client);
                    }
                }
                catch (Exception e)
                {

                }
            }
        }

        private class Tgo_Req_Method
        {
            public Tgo_Req_Method(string request, Socket client)
            {
                //TShock.Log.ConsoleInfo("did we make it?  " + req.Length + " " + (req.Length < 2));
                if (request != "") //drop request if not in correct format.
                    return;
                TSPlayer tplr = tgoUsers[client].Item1;
                //TShock.Log.ConsoleInfo(tplr.Name + " :: " + tplr.UUID);
                if (tplr == null)
                {
                    TShock.Log.ConsoleError("TShock Player is null.");
                    return;
                }
                //TShock.Log.ConsoleInfo(tplr.Name);

                //translate string to method. execute method if user has permissions.
                switch (request)
                {
                    case "HelloWorld":
                        if (tplr.HasPermission("TGO.HelloWorld"))
                            TShock.Log.ConsoleInfo("Hello World!"); //replace with method. This is an example for testing.
                        else goto default;
                        break;

                    case "Point1":
                        if (tplr.HasPermission("TGO.Point1"))
                            foreach (Command c in Commands.ChatCommands)
                                if (c.Name == "Point1")
                                    c.Run("", tplr, null);
                                else goto default;
                        break;

                    case "Point2":
                        if (tplr.HasPermission("TGO.Point2"))
                            foreach (Command c in Commands.ChatCommands)
                                if (c.Name == "Point2")
                                    c.Run("", tplr, null);
                                else goto default;
                        break;

                    case "Cut":
                        if (tplr.HasPermission("TGO.Cut"))
                            foreach (Command c in Commands.ChatCommands)
                                if (c.Name == "Cut")
                                    c.Run("", tplr, null);
                                else goto default;
                        break;

                    default:
                        TShock.Log.ConsoleError("" + tplr.Name + " tried to execute " +
                            request + " but did not have permission!");
                        break;
                }
            }

            public static TSPlayer GetTSPlayerByName(string name)
            {
                foreach (TSPlayer plr in TShock.Players)
                    if (plr.Name == name)
                        return plr;
                return null;
            }
        }
    }
}
