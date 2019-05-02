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
            var (inputSources, markerInfos) = program.ReadInput("InputData\\input_sources.csv");
            var (targets, _) = program.ReadInput("InputData\\targets.csv");

            foreach (var target in targets)
            {
                foreach (var targetMarkerValue in target.MarkerValues)
                {
                    targetMarkerValue.MarkerInfo = markerInfos.SingleOrDefault(x => x.MarkerName == targetMarkerValue.MarkerInfo.MarkerName);
                }
            }

            var readConfigs = program.ReadConfig("InputData\\configuration.csv", markerInfos, inputSources);
            var sourcesConfigs = program.ReadSourcesConfig("InputData\\sources_config.csv");

            var solutions = new List<GeoHydroSolutionOutput>();
            foreach (var targetSource in targets)
            {
                var target = new Target(targetSource);
                foreach (var config in readConfigs)
                {
                    foreach (var sourceConfiguration in sourcesConfigs)
                    {
                        var model = program.CreateModel(inputSources, markerInfos, config, target);
                        var geoHydroSolutionOutput = program.SolveTheModel(model, config, sourceConfiguration);
                        solutions.Add(geoHydroSolutionOutput);
                    }
                }
            }
            Console.WriteLine("\n\n\n\n");
            Console.WriteLine("  ---------  RESULTS: ------------");
            foreach (var geoHydroSolutionOutput in solutions)
            {
                Console.WriteLine(geoHydroSolutionOutput.TextOutput);
            }

            WriteCSV(solutions, "output.csv", markerInfos);
        }

        private static void WriteCSV(List<GeoHydroSolutionOutput> solutions, string outputCsv, List<MarkerInfo> markerinfos)
        {
            using (var f = new StreamWriter(outputCsv))
            {
                // headers
                f.WriteLine("Target;ConfigAlias;SourcesConfig;Mix;AbsoluteError;" + string.Join(';', markerinfos.Select(x => x.MarkerName)));

                // rows
                foreach (var s in solutions.OrderBy(x => x.Target.Source.Name).ThenBy(x => x.NormalizedError))
                {

                    var strings = new List<string>()
                    {
                        s.Target.Source.Name,
                        s.ConfigALias,
                        s.SourcesConfigAlias,
                        string.Join(',', s.UsedSources.OrderByDescending(x => x.Contribution).Select(x => $"{x.Source.Code}:{x.Contribution.ToString("F3")}")),
                        s.NormalizedError.ToString(),
                    };

                    var line = string.Join(';',
                                           strings.Concat(
                                                       markerinfos.Select(x => s.ResultingMix[x.MarkerName].ToString())
                                                   )
                                                  .ToArray());
                    f.WriteLine(line);
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
                    if (line.StartsWith("#"))
                    {
                        continue;
                    }
                    var split = line.Split(";");

                    if (split.Length != colNames.Count)
                    {
                        throw new Exception($"'{configurationCsv}' ERROR: bad column count on line: '{line}'");
                    }

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

        private List<SourceConfiguration> ReadSourcesConfig(string confCsv)
        {
            var confs = new List<SourceConfiguration>();

            using (var reader = File.OpenText(confCsv))
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

                    if (split.Length != colNames.Count)
                    {
                        throw new Exception($"'{confCsv}' ERROR: bad column count on line: '{line}'");
                    }
                    var conf = new SourceConfiguration();

                    conf.Alias = split[0];

                    var dict = new Dictionary<string, bool>();

                    for (int i = 1; i < split.Length; i++)
                    {
                        var used = false;
                        switch (split[i])
                        {
                            case "0":
                                used = false;
                                break;
                            case "1":
                                used = true;
                                break;
                            default:
                                throw new Exception($"Invalid value on line: '{line}'");
                        }
                        dict.Add(colNames[i], used);
                    }

                    conf.SoucesUsage = dict;

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

                    if (split.Length != markerInfos.Count + 2)
                    {
                        throw new Exception($"'{file}' ERROR: bad column count on line: '{line}'");
                    }

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



        HydroSourceValues CreateModel(List<Source> sources,
                                      List<MarkerInfo> markerInfos,
                                      HydroMixModelConfig config,
                                      Target t)
        {
            var ret = new HydroSourceValues(sources, markerInfos, config.NaValuesHandling, t);
            ret.StandardizeValues();
            return ret;
        }


        GeoHydroSolutionOutput SolveTheModel(HydroSourceValues inputValues,
                                             HydroMixModelConfig config,
                                             SourceConfiguration sourceConfiguration)
        {
            var text = new StringBuilder(
                $"\n"
                + $"              ===================================\n"
                + $"Result for target: '{inputValues.Target.Source.Name}' and configuration: '{config.ConfigAlias}' and source conf: '{sourceConfiguration.Alias}' :"
                + $"\n\n");
            var optanoConfig = new Configuration();
            optanoConfig.NameHandling = NameHandlingStyle.UniqueLongNames;
            optanoConfig.ComputeRemovedVariables = true;
            GeoHydroSolutionOutput geoHydroSolutionOutput;

            using (var scope = new ModelScope(optanoConfig))
            {
                var problem = new HydroMixProblem(inputValues, config, inputValues.Target, sourceConfiguration);

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

                    var sourcesContributions = problem.Sources.Select(s => (Source: s, Contribution: problem.SourceContribution[s].Value));
                    var resultingMix = inputValues.MarkerInfos().Select(x => x.MarkerName).ToDictionary(x => x, x => 0.0);

                    foreach (var source in sourcesContributions.Where(x => x.Contribution > 0.01).OrderByDescending(x => x.Contribution))
                    {
                        var array = source.Source.Name.Take(25).ToArray();
                        var sourceContribution = problem.SourceUsed[source.Source].Value;
                        var formattableString = $"Source({sourceContribution:F0}): {new string(array),25} | Contribution: {source.Contribution * 100:F1} ";
                        //Console.WriteLine(formattableString);
                        text.AppendLine(formattableString);

                        // todo compute 
                        foreach (var markerVal in source.Source.MarkerValues)
                        {
                            //resultingMix[markerVal.MarkerInfo.MarkerName] += markerVal.Value * markerVal.MarkerInfo.NormalizationCoefficient * source.Contribution;
                            resultingMix[markerVal.MarkerInfo.MarkerName] += markerVal.OriginalValue.Value * source.Contribution;
                        }
                    }

                    text.AppendLine();
                    var totalError = 0.0;
                    foreach (var markerInfo in inputValues.MarkerInfos().Where(x => x.Weight > 0))
                    {
                        var epsilonMarkerErrorPos = problem.PositiveErrors[markerInfo].Value;
                        var epsilonMarkerErrorNeg = problem.NegativeErrors[markerInfo].Value;
                        double epsilonMarkerError;// = Math.Max(-epsilonMarkerErrorNeg, epsilonMarkerErrorPos);

                        if (epsilonMarkerErrorNeg >= 0 && epsilonMarkerErrorPos> 0)
                        {
                            epsilonMarkerError = epsilonMarkerErrorPos;
                        }
                        else if (epsilonMarkerErrorNeg < 0 && epsilonMarkerErrorPos <= 0)
                        {
                            epsilonMarkerError = epsilonMarkerErrorNeg;
                        }
                        //else if (epsilonMarkerErrorNeg <= 0 && epsilonMarkerErrorPos >= 0)
                        //{
                        //    throw new InvalidOperationException("At least one of the epsilon errors should be zero");
                        //}
                        else
                        {
                            // both zero?
                            epsilonMarkerError = 0;
                        }


                        var denormalizedError = markerInfo.NormalizationCoefficient * epsilonMarkerError;
                        totalError += Math.Abs(epsilonMarkerError);

                        var originalTargetValue = inputValues.Target.Source[markerInfo].OriginalValue.Value;

                        var computedValue = resultingMix[markerInfo.MarkerName] + denormalizedError;

                        string diffInfo = null;
                        if (Math.Abs(computedValue - originalTargetValue) > TOLERANCE)
                        {
                            // todo cross check
                            diffInfo = $"| diffComputed/Target: ({computedValue,6:F3}/{originalTargetValue,6:F3})";
                        }

                        var diff = Math.Abs(originalTargetValue - resultingMix[markerInfo.MarkerName]);

                        var formattableString = $"Marker {markerInfo.MarkerName,10} | diff: {denormalizedError,6:F2} | mixValue: {resultingMix[markerInfo.MarkerName],6:F2} {diffInfo}";
                        text.AppendLine(formattableString);
                    }

                    //problem.Model.VariableStatistics.WriteCSV(AppDomain.CurrentDomain.BaseDirectory);
                    geoHydroSolutionOutput = new GeoHydroSolutionOutput()
                    {
                        TextOutput = text.ToString() + $"              ===================================\n" + '\n',
                        ConfigALias = config.ConfigAlias,
                        Target = inputValues.Target,
                        SourcesConfigAlias = sourceConfiguration.Alias,
                        UsedSources = sourcesContributions.Where(x => x.Contribution > 0.01).OrderByDescending(x => x.Contribution),
                        NormalizedError = totalError,
                        ResultingMix = resultingMix,
                    };
                }
            }
            return geoHydroSolutionOutput;
        }
    }

    internal class SourceConfiguration
    {
        public string Alias { get; set; }
        public Dictionary<string, bool> SoucesUsage { get; set; }
    }

    internal class GeoHydroSolutionOutput
    {
        public string TextOutput { get; set; }
        public string ConfigALias { get; set; }
        public string SourcesConfigAlias { get; set; }
        public Dictionary<string, double> ResultingMix { get; set; }
        public double NormalizedError { get; set; }
        public Target Target { get; set; }
        public IOrderedEnumerable<(Source Source, double Contribution)> UsedSources { get; set; }
    }
}
