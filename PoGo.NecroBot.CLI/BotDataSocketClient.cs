﻿using Newtonsoft.Json;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.Logging;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Tasks;
using POGOProtos.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PoGo.NecroBot.CLI
{
    public class BotDataSocketClient
    {
        private static List<EncounteredEvent> events = new List<EncounteredEvent>();
        private const int POLLING_INTERVAL = 5000;
        public static void Listen(IEvent evt, Session session)
        {
            dynamic eve = evt;

            try
            {
                HandleEvent(eve);
            }
            catch
            {
            }
        }

        private static void HandleEvent(EncounteredEvent eve)
        {
            lock (events)
            {
                events.Add(eve);
            }
        }

        private static string Serialize(dynamic evt)
        {
            var jsonSerializerSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };

            // Add custom seriaizer to convert uong to string (ulong shoud not appear to json according to json specs)
            jsonSerializerSettings.Converters.Add(new IdToStringConverter());

            string json = JsonConvert.SerializeObject(evt, Formatting.None, jsonSerializerSettings);
            //json = Regex.Replace(json, @"\\\\|\\(""|')|(""|')", match => {
            //    if (match.Groups[1].Value == "\"") return "\""; // Unescape \"
            //    if (match.Groups[2].Value == "\"") return "'";  // Replace " with '
            //    if (match.Groups[2].Value == "'") return "\\'"; // Escape '
            //    return match.Value;                             // Leave \\ and \' unchanged
            //});
            return json;
        }

        private static int retries = 0;
        static List<EncounteredEvent> processing = new List<EncounteredEvent>();

        public static async Task Start(Session session, CancellationToken cancellationToken)
        {
            await Task.Delay(30000,cancellationToken);//delay running 30s

            System.Net.ServicePointManager.Expect100Continue = false;

            cancellationToken.ThrowIfCancellationRequested();

            var socketURL = session.LogicSettings.DataSharingDataUrl;

            using (var ws = new WebSocketSharp.WebSocket(socketURL))
            {
                ws.Log.Level = WebSocketSharp.LogLevel.Fatal;
                ws.Log.Output = (logData, message) =>
                 {
                     //silenly, no log exception message to screen that scare people :)
                 };

                ws.OnMessage += (sender, e) =>
                {
                    onSocketMessageRecieved(session, sender, e);
                };

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (retries == 5) //failed to make connection to server  times contiuing, temporary stop for 10 mins.
                        {
                            session.EventDispatcher.Send(new WarnEvent()
                            {
                                Message = "Couldn't establish the connection to necro socket server, Bot will re-connect after 10 mins"
                            });
                            await Task.Delay(1 * 60 * 1000, cancellationToken);
                            retries = 0;
                        }

                        if (events.Count > 0 && ws.ReadyState != WebSocketSharp.WebSocketState.Open)
                        {
                            retries++;
                            ws.Connect();
                        }

                        while (ws.ReadyState == WebSocketSharp.WebSocketState.Open)
                        {
                            //Logger.Write("Connected to necrobot data service.");
                            retries = 0;

                            lock (events)
                            {
                                processing.Clear();
                                processing.AddRange(events);
                            }

                            if (processing.Count > 0 && ws.IsAlive)
                            {
                                if (processing.Count == 1)
                                {
                                    //serialize list will make data bigger, code ugly but save bandwidth and help socket process faster
                                    var data = Serialize(processing.First());
                                    ws.Send($"42[\"pokemon\",{data}]");
                                }
                                else {
                                    var data = Serialize(processing);
                                    ws.Send($"42[\"pokemons\",{data}]");
                                }
                            }
                            lock (events)
                            {
                                events.RemoveAll(x => processing.Any(t => t.EncounterId == x.EncounterId));
                            }
                            await Task.Delay(POLLING_INTERVAL, cancellationToken);
                            ws.Ping();
                        }
                    }
                    catch (IOException)
                    {
                        session.EventDispatcher.Send(new WarnEvent
                        {
                            Message = "Disconnect to necro socket. New connection will be established when service available..."
                        });
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        //everytime disconnected with server bot wil reconnect after 15 sec
                        await Task.Delay(POLLING_INTERVAL, cancellationToken);
                    }
                }
            }
        }
        
        private static void onSocketMessageRecieved(ISession session, object sender, WebSocketSharp.MessageEventArgs e)
        {
            try
            {
                var match = Regex.Match(e.Data, "42\\[\"pokemon\",(.*)]");
                if (match != null && !string.IsNullOrEmpty(match.Groups[1].Value))
                {
                    var data = JsonConvert.DeserializeObject<EncounteredEvent>(match.Groups[1].Value);
                    data.IsRecievedFromSocket = true;
                    session.EventDispatcher.Send(data);
                    if (session.LogicSettings.AllowAutoSnipe)
                    {   
                        var move1 = PokemonMove.Absorb;
                        var move2 = PokemonMove.Absorb;
                        Enum.TryParse< PokemonMove>( data.Move1, true, out move1);
                        Enum.TryParse<PokemonMove>(data.Move1, true, out move2);
                        MSniperServiceTask.AddSnipeItem(session, new MSniperServiceTask.MSniperInfo2()
                        {
                            Latitude = data.Latitude,
                            Longitude = data.Longitude,
                            EncounterId = Convert.ToUInt64(data.EncounterId),
                            SpawnPointId = data.SpawnPointId,
                            PokemonId = (short)data.PokemonId,
                            Iv = data.IV,
                            Move1 =  move1,
                            Move2 = move2
                        });
                    }
                    return;
                }
                match = Regex.Match(e.Data, "42\\[\"fpm\",(.*)]");
                if (match != null && !string.IsNullOrEmpty(match.Groups[1].Value))
                {
                    
                    //var data = JsonConvert.DeserializeObject<List<Logic.Tasks.HumanWalkSnipeTask.FastPokemapItem>>(match.Groups[1].Value);
                    HumanWalkSnipeTask.AddFastPokemapItem(match.Groups[1].Value);
                    return;
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                Logger.Write("ERROR TO ADD SNIPE< DEBUG ONLY " + ex.Message, LogLevel.Info, ConsoleColor.Yellow);
                #endif
            }

        }

        internal static Task StartAsync(Session session, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() => Start(session, cancellationToken), cancellationToken);
        }
    }
}
