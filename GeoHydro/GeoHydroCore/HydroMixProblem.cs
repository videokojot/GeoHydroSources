﻿using System.Linq;
using OPTANO.Modeling.Optimization;
using OPTANO.Modeling.Optimization.Enums;

namespace GeoHydroCore
{
    class HydroMixProblem
    {
        public Model Model { get; }

        public VariableCollection<Source> SourceContribution { get; set; }
        public VariableCollection<Source> SourceUsed { get; set; }

        public VariableCollection<MarkerInfo> EpsilonErrors { get; set; }

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
                s => s.MaxSourceContribution, // weights should be >= 0 <= 1
                s => OPTANO.Modeling.Optimization.Enums.VariableType.Continuous);

            SourceUsed = new VariableCollection<Source>(
                Model,
                sources,
                "SourceIsUsed",
                s => $"Indicator whether source {s.Code} is used.",
                s => 0,
                s => 1,
                s => OPTANO.Modeling.Optimization.Enums.VariableType.Binary);

            EpsilonErrors = new VariableCollection<MarkerInfo>(Model,
                                                               values.GetMarkerInfos(),
                                                               "Epsilon error for each marker", mi => $"Error for marker: {mi.MarkerName}.",
                                                               mi => double.MinValue,
                                                               mi => double.MaxValue,
                                                               mi => VariableType.Continuous
            );

            // constraints
            foreach (var source in sources)
            {
                var indicator = SourceContribution[source] + SourceUsed[source] >= 1.01 * SourceUsed[source];

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

            // equation for each marker
            foreach (var markerInfo in values.GetMarkerInfos())
            {
                var markerEpsilon = EpsilonErrors[markerInfo];
                // get all values for current marker
                var markerValues = sources.Where(x => x.MaxSourceContribution > 0).Select(s => s.MarkerValues.Single(mv => mv.MarkerInfo == markerInfo));

                var sourcesContributedToMarker = Expression.Sum(
                        markerValues.Select(x => SourceContribution[x.Source] * x.Value));

                Model.AddConstraint(sourcesContributedToMarker == markerEpsilon + target[markerInfo]);
            }

            // min: diff between target and resulting mix
            Model.AddObjective(new Objective(Expression.Sum(values.GetMarkerInfos().Where(mi => mi.Weight > 0).Select(mi => EpsilonErrors[mi] * mi.Weight)),
                                             "Difference between mix and target.",
                                             ObjectiveSense.Minimize));
        }
    }
}