using System;
using System.Collections.Generic;
using TShockAPI;
using TerrariaApi.Server;
using Terraria;
using System.Net.Sockets;
using System.Net;
using System.IO;
using System.Threading;
using System.Drawing;
using System.Text;

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
        public static Dictionary<Socket, Tuple<TSPlayer, Clipboard>> tgoUsers; //mapping of socket to a TSPlayer and their world edit clipboard.

        public override string Name => "Tgo_Requests";

        public override Version Version => new Version(1, 0);

        public override string Author => "Tsohg";

        public override string Description => "Used to handle requests from TGO GUIs.";

        public Tgo_Requests(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            tempClients = new List<Socket>();
            tgoUsers = new Dictionary<Socket, Tuple<TSPlayer, Clipboard>>();
            TShockAPI.Hooks.PlayerHooks.PlayerPostLogin += LinkTgo;
            TShockAPI.Hooks.PlayerHooks.PlayerLogout += UnlinkTgo;
            TShockAPI.Hooks.PlayerHooks.PlayerLogout += UpdateClientPlayerListOnLogout;
            TShockAPI.Hooks.PlayerHooks.PlayerPostLogin += UpdateClientPlayerListOnLogin;

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
                TShockAPI.Hooks.PlayerHooks.PlayerLogout -= UpdateClientPlayerListOnLogout;
                TShockAPI.Hooks.PlayerHooks.PlayerPostLogin -= UpdateClientPlayerListOnLogin;
            }
            base.Dispose(disposing);
        }

        private void UpdateClientPlayerListOnLogout(TShockAPI.Hooks.PlayerLogoutEventArgs args)
        {
            TShock.Log.ConsoleInfo("Updating client player list on logout...");
            UpdateClientPlayerList();
        }

        private void UpdateClientPlayerListOnLogin(TShockAPI.Hooks.PlayerPostLoginEventArgs args)
        {
            TShock.Log.ConsoleInfo("Updating client player list on login...");
            UpdateClientPlayerList();
        }

        /// <summary>
        /// Updates all clients who are listening with player info.
        /// </summary>
        private void UpdateClientPlayerList()
        {
            try
            {
                StringBuilder message = new StringBuilder("PL,"); //PL is an identifier
                foreach (TSPlayer plr in TShock.Players)
                    if(plr != null)
                        message.Append(plr.Name + ",");
                //debug
                TShock.Log.ConsoleInfo(message.ToString());
                foreach (KeyValuePair<Socket, Tuple<TSPlayer, Clipboard>> kv in tgoUsers)
                {
                    StreamWriter wr = new StreamWriter(new NetworkStream(kv.Key));
                    wr.WriteLine(message.ToString());
                    wr.Flush();
                    wr.Close(); //beware close
                }
            }
            catch (Exception e)
            {
                TShock.Log.ConsoleError("UpdateClientPlayerList Error: " + e.Message);
                return;
            }
        }

        /// <summary>
        /// Links client socket remote ip in list to TSPlayer's ip address.
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

                    //try send player list. this hook works.
                    StringBuilder message = new StringBuilder("PL,");
                    foreach (TSPlayer plr in TShock.Players)
                        if (plr != null)
                            message.Append(plr.Name + ",");
                    sw.WriteLine(message.ToString());
                    sw.Flush();
                    sw.Close(); //beware of close.

                    //Set up client listener.
                    Tgo_Client_Listener tgcl = new Tgo_Client_Listener(c);

                    //map socket to player and remove from temp. client storage
                    tgoUsers.Add(c, new Tuple<TSPlayer, Clipboard>(args.Player, new Clipboard(-1, -1, null)));
                    tempClients.Remove(c);
                }
            }
        }

        /// <summary>
        /// TODO: Disable buttons/features on logout.
        /// </summary>
        /// <param name="args"></param>
        private void UnlinkTgo(TShockAPI.Hooks.PlayerLogoutEventArgs args)
        {
            foreach (KeyValuePair<Socket, Tuple<TSPlayer, Clipboard>> tgoUser in tgoUsers)
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

        public class Clipboard
        {
            public List<ushort> tileIds; //all tileids
            public int length; //length of rectangle
            public int width; //width of rectangle

            public Clipboard(int length, int width, List<ushort> tileIds)
            {
                this.tileIds = tileIds;
                this.length = length;
                this.width = width;
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
                        TShock.Log.ConsoleInfo("Attempting to recieve a message...");
                        string request = await sr.ReadLineAsync();
                        TShock.Log.ConsoleInfo("Request Recieved: " + request);
                        //process request
                        Tgo_Req_Method req = new Tgo_Req_Method(request, client);
                    }
                }
                catch (Exception e)
                {
                    TShock.Log.ConsoleError(e.Message);
                }
            }
        }

        private class Tgo_Req_Method
        {
            public Tgo_Req_Method(string request, Socket client)
            {
                TShock.Log.ConsoleInfo("did we make it?");
                if (request == "") //drop request if not in correct format.
                    return;
                TSPlayer tplr = tgoUsers[client].Item1;
                TShock.Log.ConsoleInfo(tplr.Name + " :: " + tplr.UUID);
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
                        if (tplr.HasPermission("TGO.Point1")) //Permissions are a bit iffy.
                        {
                            foreach (Command c in Commands.ChatCommands)
                            {
                                if (c.Name == "Point1")
                                {
                                    c.Run("", tplr, null);
                                }
                            }
                        }
                        else goto default;
                        break;

                    case "Point2":
                        if (tplr.HasPermission("TGO.Point2")) //Permissions are a bit iffy.
                        {
                            foreach (Command c in Commands.ChatCommands)
                            {
                                if (c.Name == "Point2")
                                {
                                    c.Run("", tplr, null);
                                }
                            }
                        }
                        else goto default;
                        break;

                    case "Cut":
                        if (tplr.HasPermission("TGO.Cut")) //Permissions are a bit iffy.
                        {
                            foreach (Command c in Commands.ChatCommands)
                            {
                                if (c.Name == "Cut")
                                {
                                    c.Run("", tplr, null);
                                }
                            }
                        }
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
