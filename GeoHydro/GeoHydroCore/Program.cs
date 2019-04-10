using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OPTANO.Modeling.Common;
using OPTANO.Modeling.Optimization;
using OPTANO.Modeling.Optimization.Configuration;
using OPTANO.Modeling.Optimization.Solver.GLPK;

namespace GeoHydroCore
{
    class Program
    {
        // optano modelling
        // https://groups.google.com/forum/#!topic/optano-modeling/FN6V4u6wWpM
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
            Test();
        }

        static void Test()
        {
            var program = new Program();
            var (inputSources, makrerInfos) = program.ReadInput("InputData\\input_sources.csv");
            var (targets, _) = program.ReadInput("InputData\\targets.csv");

            foreach (var target in targets)
            {
                foreach (var targetMarkerValue in target.MarkerValues)
                {
                    targetMarkerValue.MarkerInfo = makrerInfos.SingleOrDefault(x => x.MarkerName == targetMarkerValue.MarkerInfo.MarkerName);
                }
            }

            var readConfigs = program.ReadConfig("InputData\\configuration.csv", makrerInfos, inputSources);

            foreach (var targetSource in targets)
            {
                foreach (var config in readConfigs)
                {
                    var model = program.CreateModel(inputSources, makrerInfos, config);

                    program.SolveTheModel(model, config, new Target(targetSource));
                }
            }
        }

        private List<HydroMixModelConfig> ReadConfig(string configurationCsv, List<MarkerInfo> markerInfos, List<Source> sources)
        {
            var confs = new List<HydroMixModelConfig>();

            using (var reader = File.OpenText(configurationCsv))
            {
                var hdrs = reader.ReadLine();
                string line;

                var colNames = hdrs.Split(';').ToList();

                while ((line = reader.ReadLine()) != null)
                {
                    var split = line.Split(";");

                    var conf = new HydroMixModelConfig()
                    {
                        MinimalSourceContribution = double.Parse(split[0]),
                        MinSourcesUsed = int.Parse(split[1]),
                        MaxSourcesUsed = int.Parse(split[2]),
                    };

                    var parsed = Enum.TryParse<NAValuesHandling>(split[3], out var res);

                    if (!parsed)
                    {
                        throw new InvalidOperationException("NA values handling unknown value: either 'Remove' or 'SetMean'");
                    }
                    conf.NaValuesHandling = res;

                    for (int i = 4; i < split.Length; i++)
                    {
                        var src = sources.SingleOrDefault(x => x.Code == colNames[i]);
                        var mi = markerInfos.SingleOrDefault(x => x.MarkerName == colNames[i]);

                        if (src == null && mi == null)
                        {
                            throw new InvalidOperationException("Unknown col name " + colNames[i]);
                        }
                        double val;

                        if (string.IsNullOrEmpty(split[i]))
                        {
                            val = 1;
                        }
                        else
                        {
                            val = double.Parse(split[i]);
                        }

                        if (src != null)
                        {
                            if (val > 1 || val < 0)
                            {
                                throw new InvalidOperationException("Source max contribution must be between 0 and 1!");
                            }
                            src.MaxSourceContribution = val;
                        }

                        if (mi != null)
                        {
                            if (val < 0)
                            {
                                throw new InvalidOperationException("Marker weight cannot be negative!");
                            }
                            mi.Weight = val;
                        }
                    }
                    confs.Add(conf);
                }
            }
            return confs;
        }

        (List<Source>, List<MarkerInfo>) ReadInput(string file)
        {
            var sources = new List<Source>();
            List<MarkerInfo> markerInfos;

            using (var reader = File.OpenText(file))
            {
                var hdrs = reader.ReadLine();

                markerInfos = hdrs.Split(';').Skip(2).Select(x => new MarkerInfo()
                {
                    MarkerName = x,
                }).ToList();

                string line;
                int lnCount = 1;
                while ((line = reader.ReadLine()) != null)
                {
                    lnCount++;
                    var split = line.Split(';');
                    var srcCode = split[0];
                    var srcName = split[1];

                    var source = new Source()
                    {
                        Code = srcCode,
                        Name = srcName,
                        MarkerValues = new List<MarkerValue>()
                    };

                    for (int i = 0; i < markerInfos.Count; i++)
                    {
                        var markerInfo = markerInfos[i];
                        var markerVal = split[i + 2];
                        double? val = null;

                        if (double.TryParse(markerVal, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        {
                            val = v;
                        }
                        else if (markerVal == "NA")
                        {
                            val = null;
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unexpected values on line: {lnCount} , column: {i} ");
                        }

                        source.MarkerValues.Add(new MarkerValue()
                        {
                            MarkerInfo = markerInfo,
                            OriginalValue = val,
                            Source = source,
                        });
                    }
                    sources.Add(source);
                }
            }
            return (sources, markerInfos);
        }

        HydroSourceValues CreateModel(List<Source> sources, List<MarkerInfo> markerInfos, HydroMixModelConfig config)
        {
            var ret = new HydroSourceValues(sources, markerInfos, config.NaValuesHandling);
            ret.StandardizeValues();
            return ret;
        }


        void SolveTheModel(HydroSourceValues inputValues,
                           HydroMixModelConfig config,
                           Target target)
        {
            var optanoConfig = new Configuration();
            optanoConfig.NameHandling = NameHandlingStyle.UniqueLongNames;
            optanoConfig.ComputeRemovedVariables = true;
            using (var scope = new ModelScope(optanoConfig))
            {
                var problem = new HydroMixProblem(inputValues, config, target);
                using (var solver = new GLPKSolver())
                {
                    //solver.
                    // solve the model
                    var solution = solver.Solve(problem.Model);

                    // import the results back into the model 
                    foreach (var vc in problem.Model.VariableCollections)
                    {
                        vc.SetVariableValues(solution.VariableValues);
                    }

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
}
