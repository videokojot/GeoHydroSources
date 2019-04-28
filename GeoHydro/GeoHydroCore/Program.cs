using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OPTANO.Modeling.Common;
using OPTANO.Modeling.Optimization;
using OPTANO.Modeling.Optimization.Configuration;
using OPTANO.Modeling.Optimization.Solver.GLPK;

namespace GeoHydroCore
{
    class Program
    {
        // glpk library referencing
        // https://groups.google.com/forum/#!topic/optano-modeling/FN6V4u6wWpM
        static void Main(string[] args)
        {
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
            var solutions = new List<GeoHydroSolutionOutput>();
            foreach (var targetSource in targets)
            {
                var target = new Target(targetSource);
                foreach (var config in readConfigs)
                {
                    var model = program.CreateModel(inputSources, makrerInfos, config, target);

                    solutions.Add(program.SolveTheModel(model, config));
                }
            }
            Console.WriteLine("\n\n\n\n");
            Console.WriteLine("  ---------  RESULTS: ------------");
            foreach (var geoHydroSolutionOutput in solutions)
            {
                Console.WriteLine(geoHydroSolutionOutput.TextOutput);
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
                    if (line.StartsWith("#"))
                    {
                        continue;
                    }
                    var split = line.Split(";");

                    var pos = 0;
                    var conf = new HydroMixModelConfig();

                    conf.ConfigAlias = split[pos++];
                    conf.MinimalSourceContribution = double.Parse(split[pos++], CultureInfo.InvariantCulture);
                    conf.MinSourcesUsed = int.Parse(split[pos++]);
                    conf.MaxSourcesUsed = int.Parse(split[pos++]);

                    var parsed = Enum.TryParse<NAValuesHandling>(split[pos++], out var res);
                    if (!parsed)
                    {
                        throw new InvalidOperationException("NA values handling unknown value: either 'Remove' or 'SetMean'");
                    }
                    conf.NaValuesHandling = res;

                    for (int i = pos; i < split.Length; i++)
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
                            val = double.Parse(split[i], CultureInfo.InvariantCulture);
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
                    if (line.StartsWith("#"))
                    {
                        continue;
                    }
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

        HydroSourceValues CreateModel(List<Source> sources, List<MarkerInfo> markerInfos, HydroMixModelConfig config, Target t)
        {
            var ret = new HydroSourceValues(sources, markerInfos, config.NaValuesHandling, t);
            ret.StandardizeValues();
            return ret;
        }


        GeoHydroSolutionOutput SolveTheModel(HydroSourceValues inputValues, HydroMixModelConfig config)
        {
            var text = new StringBuilder(
                $"\n"
                + $"              ===================================\n"
                + $"Result for target: '{inputValues.Target.Source.Name}' and configuration: {config.ConfigAlias}"
                + $"\n");
            var optanoConfig = new Configuration();
            optanoConfig.NameHandling = NameHandlingStyle.UniqueLongNames;
            optanoConfig.ComputeRemovedVariables = true;
            using (var scope = new ModelScope(optanoConfig))
            {
                var problem = new HydroMixProblem(inputValues, config, inputValues.Target);
                using (var solver = new GLPKSolver())
                {
                    // solve the model
                    var solution = solver.Solve(problem.Model);
                    // import the results back into the model 
                    foreach (var vc in problem.Model.VariableCollections)
                    {
                        vc.SetVariableValues(solution.VariableValues);
                    }

                    // print objective and variable decisions
                    //Console.WriteLine($"{solution.ObjectiveValues.Single()}");
                    var TOLERANCE = 0.001;
                    var usedSources = inputValues.Sources().Where(s => Math.Abs(problem.SourceUsed[s].Value - 1) < TOLERANCE).ToList();
                    var unusedSources = inputValues.Sources().Where(s => Math.Abs(problem.SourceUsed[s].Value) < TOLERANCE).ToList();

                    var sourcesContributions = inputValues.Sources().Select(s => (Source: s, Contribution: problem.SourceContribution[s].Value));

                    foreach (var source in sourcesContributions.Where(x => x.Contribution > 0.01).OrderByDescending(x => x.Contribution))
                    {
                        var array = source.Source.Name.Take(25).ToArray();
                        var formattableString = $"Source({problem.SourceUsed[source.Source].Value:F0}): {new string(array),25} | Contribution: {source.Contribution * 100:F0} ";
                        //Console.WriteLine(formattableString);
                        text.AppendLine(formattableString);
                    }

                    text.AppendLine();

                    foreach (var markerInfo in inputValues.MarkerInfos().Where(x => x.Weight > 0))
                    {
                        var epsilonMarkerError = problem.EpsilonErrors[markerInfo].Value;
                        var absoluteValue = markerInfo.NormalizationCoefficient * epsilonMarkerError;
                        //Console.WriteLine($"Marker '{markerInfo.MarkerName,10}' error contribution: {epsilonMarkerError} standardized. Aboslute {absoluteValue}");
                        var formattableString = $"Marker {markerInfo.MarkerName,10} diff: {absoluteValue,6:F2}";
                        //Console.WriteLine(formattableString);
                        text.AppendLine(formattableString);
                    }

                    //problem.Model.VariableStatistics.WriteCSV(AppDomain.CurrentDomain.BaseDirectory);
                }
            }
            return new GeoHydroSolutionOutput()
            {
                TextOutput = text.ToString() + $"              ===================================\n" + '\n',
            };
        }
    }

    internal class GeoHydroSolutionOutput
    {
        public string TextOutput { get; set; }
    }
}
