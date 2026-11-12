using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UOClient.Net;

// Usage: NetTest <host> <port> <account> <password> [characterName]
// Logs in, picks the first server, prints the character list, and optionally enters the world.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "map")
        {
            return MapProbe.Run(args);
        }

        if (args.Length > 0 && args[0] == "houses")
        {
            return HousesProbe.Run(args);
        }

        if (args.Length > 0 && args[0] == "house")
        {
            return HouseProbe.Run(args);
        }

        if (args.Length > 0 && args[0] == "statics")
        {
            return StaticsProbe.Run(args);
        }

        if (args.Length > 0 && args[0] == "art")
        {
            return ArtProbe.Run(args);
        }

        if (args.Length > 0 && args[0] == "tilenames")
        {
            return TileNames.Run(args);
        }

        if (args.Length > 0 && args[0] == "landcensus")
        {
            return LandCensus.Run(args);
        }

        if (args.Length > 0 && args[0] == "census")
        {
            return StaticCensus.Run(args);
        }

        if (args.Length > 0 && args[0] == "waterholes")
        {
            return WaterHoles.Run(args);
        }

        if (args.Length > 0 && args[0] == "walk")
        {
            return WalkProbe.Run(args);
        }

        if (args.Length < 4)
        {
            Console.WriteLine("usage: NetTest <host> <port> <account> <password> [characterName]");
            return 2;
        }

        string character = args.Length > 4 ? args[4] : null;
        int exitCode = -1;
        var session = new UoSession();

        session.StateChanged += state => Console.WriteLine($"state: {state}");
        session.Failed += message => { Console.WriteLine($"FAILED: {message}"); exitCode = 1; };
        session.ServerListReceived += servers =>
        {
            Console.WriteLine("servers: " + string.Join(", ", servers.Select(s => $"[{s.Index}] {s.Name}")));
            session.SelectServer(servers[0]);
        };
        session.CharacterListReceived += characters =>
        {
            Console.WriteLine($"characters ({characters.Count}): " +
                              string.Join(", ", characters.Select(c => $"[{c.Slot}] {c.Name}")));
            Console.WriteLine("cities: " + string.Join(", ", session.Cities.Select(c => c.City)));
            CharacterSlot pick = characters.FirstOrDefault(c => c.Name == character);
            if (pick == null)
            {
                if (character == null)
                {
                    exitCode = 0;
                    return;
                }

                Console.WriteLine($"creating character {character}");
                session.CreateCharacter(new NewCharacter { Name = character, CityIndex = 3 });
                return;
            }

            session.SelectCharacter(pick);
        };
        session.EnteredWorld += confirm =>
        {
            Console.WriteLine($"in world: serial 0x{confirm.Serial:X8} body {confirm.Body} at " +
                              $"{confirm.X},{confirm.Y},{confirm.Z} map {confirm.MapWidth}x{confirm.MapHeight}");
        };
        int afterWorld = 0;
        session.PacketReceived += packet =>
        {
            Console.WriteLine($"  packet 0x{packet.Id:X2} ({packet.Length} bytes)");
            if (session.State == SessionState.InWorld && packet.Id == 0x55)
            {
                afterWorld = 1;
            }
        };

        session.Login(args[0], int.Parse(args[1]), args[2], args[3]);

        var clock = Stopwatch.StartNew();
        while (exitCode < 0 && clock.ElapsedMilliseconds < 20000)
        {
            session.Poll();
            if (afterWorld == 1)
            {
                // Drain a moment longer so the initial world burst is visible, then stop.
                Thread.Sleep(500);
                session.Poll();
                exitCode = 0;
            }

            Thread.Sleep(10);
        }

        if (exitCode < 0)
        {
            Console.WriteLine($"TIMEOUT in state {session.State}");
            exitCode = 3;
        }

        session.Dispose();
        return exitCode;
    }
}
