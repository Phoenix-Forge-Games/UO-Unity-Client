using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UOClient.Data;
using UOClient.Net;
using UOClient.World;

// NetTest walk <host> <port> <account> <password> <character> <dataFolder> <directions>
// Enters the world, then steps through a direction string (N/E/S/W, lowercase = run), paced like
// the Unity client, and reports how the server answered each step.
internal static class WalkProbe
{
    public static int Run(string[] args)
    {
        string character = args[5];
        string dataFolder = args[6];
        string route = args[7];

        var session = new UoSession();
        var world = new WorldState(session.Send);
        var tiles = new TileData(Path.Combine(dataFolder, "tiledata.mul"));
        MapFacet map = null;
        int denials = 0;
        bool ready = false;

        session.PacketReceived += world.Handle;
        session.Failed += message => Console.WriteLine($"FAILED: {message}");
        session.ServerListReceived += servers => session.SelectServer(servers[0]);
        session.CharacterListReceived += list =>
        {
            foreach (CharacterSlot slot in list)
            {
                if (slot.Name == character)
                {
                    session.SelectCharacter(slot);
                    return;
                }
            }

            Console.WriteLine($"no character named {character}");
        };
        session.EnteredWorld += confirm => world.Begin(confirm);
        session.PacketReceived += packet =>
        {
            if (packet.Id == 0x55)
            {
                ready = true;
            }
        };
        world.PlayerTeleported += player =>
        {
            if (ready)
            {
                denials++;
                Console.WriteLine($"  server moved/refused -> {player.X},{player.Y},{player.Z} facing {player.Facing}");
            }
        };

        session.Login(args[1], int.Parse(args[2]), args[3], args[4]);
        var clock = Stopwatch.StartNew();
        while (!ready && clock.ElapsedMilliseconds < 15000)
        {
            session.Poll();
            Thread.Sleep(10);
        }

        if (!ready)
        {
            Console.WriteLine("never reached the world");
            return 3;
        }

        map = new MapFacet(dataFolder, world.Facet);
        var surfaces = new SurfaceQuery(map, tiles);
        world.Walker.StandingZ = surfaces.StandingZ;
        Mobile player = world.Player;
        Console.WriteLine($"facet {world.Facet}, start {player.X},{player.Y},{player.Z} facing {player.Facing}");

        foreach (char c in route)
        {
            Direction direction;
            switch (char.ToUpperInvariant(c))
            {
                case 'N': direction = Direction.North; break;
                case 'E': direction = Direction.East; break;
                case 'S': direction = Direction.South; break;
                case 'W': direction = Direction.West; break;
                default: continue;
            }

            bool run = char.IsLower(c);
            // Turn first if needed, then step, with the same pacing as the Unity client.
            while (player.Facing != direction)
            {
                world.Walker.TryStep(direction, run);
                Pump(session, 100);
            }

            bool sent = world.Walker.TryStep(direction, run);
            Console.WriteLine($"{c}: {(sent ? "sent" : "blocked locally")} -> {player.X},{player.Y},{player.Z}");
            Pump(session, run ? 200 : 400);
        }

        Pump(session, 600);
        Console.WriteLine($"end {player.X},{player.Y},{player.Z}; pending acks {world.Walker.PendingSteps}; refusals {denials}");
        session.Dispose();
        map.Dispose();
        return denials == 0 && world.Walker.PendingSteps == 0 ? 0 : 1;
    }

    private static void Pump(UoSession session, int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            session.Poll();
            Thread.Sleep(5);
        }
    }
}
