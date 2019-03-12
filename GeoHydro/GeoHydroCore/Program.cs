using System;
using OPTANO.Modeling.Optimization;
using OPTANO.Modeling.Optimization.Solver.GLPK;

namespace GeoHydroCore
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            var model = new Model();

            var solver = new GLPKSolver();


            // TODOs:
            // define model
            // normalize markers
            // read config


            // model:
            // weights sum should be 1
            // weights should be >= 0 <= 1
            // indicator variables (whether source is used)
            // count of used sources (if defined)
            // equation for each marker
            // min: diff between target and resulting mix
        }
    }
}
