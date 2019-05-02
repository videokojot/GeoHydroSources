using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GeoHydroCore
{
    internal class ConfigReader
    {
        public List<HydroMixModelConfig> ReadConfig(string configurationCsv, List<MarkerInfo> markerInfos, List<Source> sources)
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

        public List<SourceConfiguration> ReadSourcesConfig(string confCsv)
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

        public (List<Source>, List<MarkerInfo>) ReadInput(string file)
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
    }
}