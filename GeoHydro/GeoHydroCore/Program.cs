using System;
using System.Collections.Generic;
using System.Linq;
using OPTANO.Modeling.Common;
using OPTANO.Modeling.Optimization;
using OPTANO.Modeling.Optimization.Configuration;
using OPTANO.Modeling.Optimization.Solver.GLPK;

namespace GeoHydroCore
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            // TODO:
            // load data from CSV
            // load configuration
            // do normalization of markers
            // do source weighting (ie. max contribution of source)

            // create model
            // solve model
            // return results (csv + info about run configuration)
        }

        static void SolveTheModel(HydroMixProblem problem)
        {
            // Use long names for easier debugging/model understanding.
            var config = new Configuration();
            config.NameHandling = NameHandlingStyle.UniqueLongNames;
            config.ComputeRemovedVariables = true;
            using (var scope = new ModelScope(config))
            {

                // Get a solver instance, change your solver
                using (var solver = new GLPKSolver())
                {
                    // solve the model
                    var solution = solver.Solve(problem.Model);

                    // import the results back into the model 
                    problem.Model.VariableCollections.ForEach(vc => vc.SetVariableValues(solution.VariableValues));

                    // print objective and variable decisions
                    Console.WriteLine($"{solution.ObjectiveValues.Single()}");
                    //problem..Variables.ForEach(x => Console.WriteLine($"{x.ToString().PadRight(36)}: {x.Value}"));
                    //problem..Variables.ForEach(y => Console.WriteLine($"{y.ToString().PadRight(36)}: {y.Value}"));

                    problem.Model.VariableStatistics.WriteCSV(AppDomain.CurrentDomain.BaseDirectory);
                    Console.ReadLine();
                }
            }
        }
    }

    class HydroSourceValues
    {
        public List<(string marker, List<MarkerSourceValue> values)> GetMarkers()
        {
            return _vals.Keys
                        .GroupBy(x => x.Marker)
                        .Select(x => (x.Key, x.Select(c => new MarkerSourceValue()
                        {
                            Marker = c.Marker,
                            Source = c.Source,
                            Value = _vals[c],
                        }).ToList()))
                        .ToList();
        }


        Dictionary<SourceMarker, double> _vals = new Dictionary<SourceMarker, double>();

        public List<string> GetSources()
        {
            return _vals.Keys.Select(k => k.Source).Distinct().ToList();
        }

        public double this[string source, string marker]
        {
            set
            {
                var sourceMarker = new SourceMarker(source, marker);

                if (_vals.ContainsKey(sourceMarker))
                {
                    throw new InvalidOperationException("Already exists");
                }
                _vals.Add(sourceMarker, value);
            }
        }
    }

    struct SourceMarker
    {
        public SourceMarker(string source, string marker)
        {
            Source = source;
            Marker = marker;
        }

        public string Source { get; set; }
        public string Marker { get; set; }
    }

    class MarkerSourceValue
    {
        public string Marker { get; set; }
        public string Source { get; set; }
        public double Value { get; set; }
    }

    class HydroMixModelConfig
    {
        public int? MinSourcesUsed { get; set; }
        public int? MaxSourcesUsed { get; set; }

        public double MinimalSourceContribution { get; set; }
    }

    class Target
    {
        public double this[string marker] { get { return 0; } }
    }

    class HydroMixProblem
    {
        public Model Model { get; }

        public VariableCollection<string> SourceContribution { get; set; }
        public VariableCollection<string> SourceUsed { get; set; }

        public HydroMixProblem(HydroSourceValues values, HydroMixModelConfig config, Target target)
        {
            Model = new Model();
            var sources = values.GetSources();
            SourceContribution = new VariableCollection<string>(
                Model,
                sources,
                "SourceContributions",
                s => $"Source {s} contribution",
                s => 0,
                s => 1, // weights should be >= 0 <= 1
                s => OPTANO.Modeling.Optimization.Enums.VariableType.Continuous);

            SourceUsed = new VariableCollection<string>(
                Model,
                sources,
                "SourceIsUsed",
                s => $"Indicator whether source {s} is used.",
                s => 0,
                s => 1,
                s => OPTANO.Modeling.Optimization.Enums.VariableType.Binary);

            // constraints
            foreach (var source in sources)
            {
                var indicator = SourceContribution[source] >= 2 * (1 - SourceUsed[source]);

                Model.AddConstraint(indicator,
                                    $"Indicator for {source}");

                if (config.MinimalSourceContribution > 0)
                {
                    Model.AddConstraint(SourceContribution[source] >= config.MinimalSourceContribution * SourceUsed[source],
                                        "Minimal source contribution.");
                }
            }

            // weights sum should be 1
            var contributionsSum = Expression.Sum(sources.Select(s => SourceContribution[s]));
            Model.AddConstraint(contributionsSum == 1, "Contributions sum equals 1");

            var usedSourcesCount = Expression.Sum(sources.Select(s => SourceUsed[s]));

            if (config.MaxSourcesUsed.HasValue)
            {
                Model.AddConstraint(usedSourcesCount <= config.MaxSourcesUsed.Value);
            }

            if (config.MinSourcesUsed.HasValue)
            {
                Model.AddConstraint(usedSourcesCount >= config.MinSourcesUsed.Value);
            }

            var epsilons = new List<Variable>();

            // equation for each marker
            foreach (var markerVal in values.GetMarkers())
            {
                var markerEpsilon = new Variable($"Diff for {markerVal.marker}", 0);
                var sourcesContributedToMarker = Expression.Sum(markerVal.values.Select(x => SourceContribution[x.Source] * x.Value));
                Model.AddConstraint(sourcesContributedToMarker == markerEpsilon + target[markerVal.marker]);
                epsilons.Add(markerEpsilon);
            }

            // min: diff between target and resulting mix
            Model.AddObjective(new Objective(Expression.Sum(epsilons), "Difference between mix and target."));
        }
    }

}
