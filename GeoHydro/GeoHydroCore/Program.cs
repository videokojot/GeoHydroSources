using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            if (args.Length == 1)
            {
                Run(args[0]);
            }
            else
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                Run($"output_{timestamp}.csv");
            }
        }

        static void Run(string outputCsvFile)
        {
            var configReader = new ConfigReader();
            var (inputSources, markerInfos) = configReader.ReadInput("InputData\\input_sources.csv");
            var (targets, _) = configReader.ReadInput("InputData\\targets.csv");

            foreach (var target in targets)
            {
                foreach (var targetMarkerValue in target.MarkerValues)
                {
                    targetMarkerValue.MarkerInfo = markerInfos.SingleOrDefault(x => x.MarkerName == targetMarkerValue.MarkerInfo.MarkerName);
                }
            }

            var readConfigs = configReader.ReadConfig("InputData\\configuration.csv");
            var sourcesConfigs = configReader.ReadSourcesConfig("InputData\\sources_config.csv");

            var solutions = new List<GeoHydroSolutionOutput>();
            var program = new Program();
            foreach (var targetSource in targets)
            {
                var target = new Target(targetSource);
                foreach (var config in readConfigs)
                {
                    foreach (var sourceConfiguration in sourcesConfigs)
                    {
                        var model = program.CreateModel(inputSources, markerInfos, config, target, sourceConfiguration);
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

            if (outputCsvFile != null)
            {
                WriteCSV(solutions, outputCsvFile, markerInfos);
            }
        }

        private static void WriteCSV(List<GeoHydroSolutionOutput> solutions, string outputCsv, List<MarkerInfo> markerinfos)
        {
            using (var f = new StreamWriter(outputCsv))
            {
                // headers
                f.WriteLine("Target;ConfigAlias;SourcesConfig;Mix;AbsoluteError;OptimalizedError;" + string.Join(';', markerinfos.Select(x => x.MarkerName)));

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
                        s.OptimalizedError.ToString()
                    };

                    var line = string.Join(';',
                                           strings.Concat(
                                                       markerinfos.Select(x => s.ResultingMix[x.MarkerName].ToString())
                                                   )
                                                  .ToArray());
                    f.WriteLine(line);
                    f.Flush();
                }
            }
        }



        HydroSourceValues CreateModel(List<Source> sources,
                                      List<MarkerInfo> markerInfos,
                                      HydroMixModelConfig config,
                                      Target t, SourceConfiguration sourcesConfiguration)
        {
            var ret = new HydroSourceValues(sources, markerInfos, config.NaValuesHandling, t);
            ret.StandardizeValues();

            foreach (var configMarkerWeigth in config.MarkerWeigths)
            {
                var mi = markerInfos.Single(x => x.MarkerName == configMarkerWeigth.Key);
                if (configMarkerWeigth.Value < 0)
                {
                    throw new InvalidOperationException("Marker weight cannot be negative!");
                }

                mi.Weight = configMarkerWeigth.Value;
            }

            foreach (var src in sources)
            {
                var val = sourcesConfiguration.MaxSourceContribution[src.Code];

                if (val > 1 || val < 0)
                {
                    throw new InvalidOperationException("Source max contribution must be between 0 and 1!");
                }
                src.MaxSourceContribution = val;
            }

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

                    var TOLERANCE = 0.001;

                    var sourcesContributions = problem.Sources.Select(s => (Source: s, Contribution: problem.SourceContribution[s].Value)).ToList();
                    var resultingMix = inputValues.MarkerInfos().Select(x => x.MarkerName).ToDictionary(x => x, x => 0.0);

                    foreach (var source in sourcesContributions.Where(x => x.Contribution > 0.01).OrderByDescending(x => x.Contribution))
                    {
                        var array = source.Source.Name.Take(25).ToArray();
                        var sourceContribution = problem.SourceUsed[source.Source].Value;
                        var formattableString = $"Source({sourceContribution:F0}): {new string(array),25} | Contribution: {source.Contribution * 100:F1} ";
                        text.AppendLine(formattableString);

                        foreach (var markerVal in source.Source.MarkerValues)
                        {
                            resultingMix[markerVal.MarkerInfo.MarkerName] += markerVal.Value * markerVal.MarkerInfo.NormalizationCoefficient * source.Contribution;
                        }
                    }

                    text.AppendLine();
                    var totalError = 0.0;
                    var optimizedError = 0.0;
                    foreach (var markerInfo in inputValues.MarkerInfos()
                                                           //.Where(x => x.Weight > 0)
                                                           )
                    {
                        var epsilonMarkerErrorPos = problem.PositiveErrors[markerInfo].Value;
                        var epsilonMarkerErrorNeg = problem.NegativeErrors[markerInfo].Value;

                        totalError += Math.Abs(epsilonMarkerErrorNeg) + Math.Abs(epsilonMarkerErrorPos);

                        if (markerInfo.Weight > 0)
                        {
                            optimizedError += Math.Abs(epsilonMarkerErrorNeg) + Math.Abs(epsilonMarkerErrorPos);
                        }

                        var originalTargetValue = inputValues.Target.Source[markerInfo].OriginalValue.Value;

                        var computedValue = resultingMix[markerInfo.MarkerName] - (epsilonMarkerErrorPos * markerInfo.NormalizationCoefficient) + (epsilonMarkerErrorNeg * markerInfo.NormalizationCoefficient);

                        string diffInfo = null;
                        if (Math.Abs(computedValue - originalTargetValue) > TOLERANCE)
                        {
                            diffInfo = $"| diffComputed/Target: ({computedValue,6:F3}/{originalTargetValue,6:F3})";
                        }

                        var realDiff = resultingMix[markerInfo.MarkerName] - originalTargetValue;

                        var formattableString = $"Marker({markerInfo.Weight:F0}) {markerInfo.MarkerName,10} | targetVal: {originalTargetValue,6:F2}  | diff: ({realDiff,6:F2}) | mixValue: {resultingMix[markerInfo.MarkerName],6:F2} {diffInfo}";
                        text.AppendLine(formattableString);
                    }

                    geoHydroSolutionOutput = new GeoHydroSolutionOutput()
                    {
                        TextOutput = text + $"              ===================================\n" + '\n',
                        ConfigALias = config.ConfigAlias,
                        Target = inputValues.Target,
                        SourcesConfigAlias = sourceConfiguration.Alias,
                        UsedSources = sourcesContributions.Where(x => x.Contribution > 0.01).OrderByDescending(x => x.Contribution),
                        NormalizedError = totalError,
                        ResultingMix = resultingMix,
                        OptimalizedError = optimizedError
                    };
                }
            }
            return geoHydroSolutionOutput;
        }
    }
}
