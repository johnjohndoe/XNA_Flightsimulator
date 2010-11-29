using XNAseries2;
using System;


namespace XNA_Flightsimulator
{
    static class Program
    {
        static void Main(string[] args)
        {
            using (Game1 game = new Game1())
            {
                game.Run();
            }
        }
    }
}

