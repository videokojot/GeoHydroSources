using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OPTANO.Modeling.Common;
using OPTANO.Modeling.Optimization;
using OPTANO.Modeling.Optimization.Configuration;
using OPTANO.Modeling.Optimization.Solver.GLPK;

namespace GeoHydroCore
{
    class MarkerInfo
    {
        public string MarkerName { get; set; }
        public double Weight { get; set; } = 1.0;
    }

    class Program
    {
        static void Main(string[] args)
        {
            // TODO:
            // load data from CSV
            // load configuration
            // do normalization of markers
            // do source weighting (ie. max contribution of source)

            // create model
            // solve model
            // return results (csv + info about run configuration)
        }


        static List<Source> ReadInput(string file)
        {
            var sources = new List<Source>();
            using (var reader = File.OpenText(file))
            {
                var hdrs = reader.ReadLine();
                var markerNames = hdrs.Split(';').Skip(2).Select(x => new MarkerInfo()
                {
                    MarkerName = x,
                }).ToList();

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var split = line.Split(';');
                    var srcCode = split[0];
                    var srcName = split[1];

                    var source = new Source()
                    {
                        Code = srcCode,
                        Name = srcName,
                        MarkerValues = new List<MarkerValue>()
                    };

                    for (int i = 0; i < markerNames.Count; i++)
                    {
                        var markerInfo = markerNames[i];
                        var markerVal = split[i + 2];
                        double? val = null;
                        if (int.TryParse(markerVal, out var v))
                        {
                            val = v;
                        }

                        source.MarkerValues.Add(new MarkerValue()
                        {
                            MarkerInfo = markerInfo,
                            Value = val,
                            Source = source,
                        });
                    }
                    sources.Add(source);
                }
            }
            return sources;
        }
        

        static void SolveTheModel(HydroMixProblem problem)
        {
            var config = new Configuration();
            config.NameHandling = NameHandlingStyle.UniqueLongNames;
            config.ComputeRemovedVariables = true;
            using (var scope = new ModelScope(config))
            {
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

    internal class Source
    {
        public List<MarkerValue> MarkerValues { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public double Weight { get; set; } = 1.0;
    }

    internal class MarkerValue
    {
        public MarkerInfo MarkerInfo { get; set; }
        public double? Value { get; set; }
        public Source Source { get; set; }
    }

    class HydroSourceValues
    {
        public List<Source> GetSourcesList()
        {
            throw new NotImplementedException();
        }

        public List<MarkerInfo> GetMarkerInfos()
        {
            throw new NotImplementedException();
        }
    }

    class HydroMixModelConfig
    {
        public int? MinSourcesUsed { get; set; }
        public int? MaxSourcesUsed { get; set; }

        public double MinimalSourceContribution { get; set; }
    }

    class Target
    {
        public double this[MarkerInfo marker] { get { return 0; } }
    }

    class HydroMixProblem
    {
        public Model Model { get; }

        public VariableCollection<Source> SourceContribution { get; set; }
        public VariableCollection<Source> SourceUsed { get; set; }

        public HydroMixProblem(HydroSourceValues values, HydroMixModelConfig config, Target target)
        {
            Model = new Model();
            //var sources = values.GetSources();
            var sources = values.GetSourcesList();
            SourceContribution = new VariableCollection<Source>(
                Model,
                sources,
                "SourceContributions",
                s => $"Source {s.Code} contribution",
                s => 0,
                s => 1, // weights should be >= 0 <= 1
                s => OPTANO.Modeling.Optimization.Enums.VariableType.Continuous);

            SourceUsed = new VariableCollection<Source>(
                Model,
                sources,
                "SourceIsUsed",
                s => $"Indicator whether source {s.Code} is used.",
                s => 0,
                s => 1,
                s => OPTANO.Modeling.Optimization.Enums.VariableType.Binary);

            // constraints
            foreach (var source in sources)
            {
                var indicator = SourceContribution[source] >= 2 * (1 - SourceUsed[source]);

                Model.AddConstraint(indicator, $"Indicator for {source.Code}");

                if (config.MinimalSourceContribution > 0)
                {
                    Model.AddConstraint(SourceContribution[source] >= config.MinimalSourceContribution * SourceUsed[source],
                                        $"Minimal source {source.Code} contribution.");
                }
            }

            // weights sum should be 1
            var contributionsSum = Expression.Sum(sources.Select(s => SourceContribution[s]));
            Model.AddConstraint(contributionsSum == 1, "Contributions sum equals 1");

            // max sources contributing
            var usedSourcesCount = Expression.Sum(sources.Select(s => SourceUsed[s]));
            if (config.MaxSourcesUsed.HasValue)
            {
                Model.AddConstraint(usedSourcesCount <= config.MaxSourcesUsed.Value);
            }

            // min sources contributing
            if (config.MinSourcesUsed.HasValue)
            {
                Model.AddConstraint(usedSourcesCount >= config.MinSourcesUsed.Value);
            }

            var epsilons = new List<Variable>();

            // equation for each marker
            foreach (var markerInfo in values.GetMarkerInfos())
            {
                var markerEpsilon = new Variable($"Diff for {markerInfo.MarkerName}", 0);

                // get all values for current marker
                var markerValues = sources.Select(s => s.MarkerValues.Single(mv => mv.MarkerInfo == markerInfo));

                var sourcesContributedToMarker =
                    Expression.Sum(
                        markerValues.Select(x => x.Source.Weight * SourceContribution[x.Source] * x.Value.Value * x.MarkerInfo.Weight));

                Model.AddConstraint(sourcesContributedToMarker == markerEpsilon + target[markerInfo]);

                epsilons.Add(markerEpsilon);
            }

            // min: diff between target and resulting mix
            Model.AddObjective(new Objective(Expression.Sum(epsilons), "Difference between mix and target."));
        }
    }

}
