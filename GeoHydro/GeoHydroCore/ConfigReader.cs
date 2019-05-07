using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GeoHydroCore
{
    internal class ConfigReader
    {
        public List<HydroMixModelConfig> ReadConfig(string configurationCsv)
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
                    conf.MarkerWeigths = new Dictionary<string, double>();

                    var parsed = Enum.TryParse<NAValuesHandling>(split[pos++], out var res);
                    if (!parsed)
                    {
                        throw new InvalidOperationException("NA values handling unknown value: either 'Remove' or 'SetMean'");
                    }
                    conf.NaValuesHandling = res;

                    for (int i = pos; i < split.Length; i++)
                    {
                        var colName = colNames[i];

                        double val;

                        val = double.Parse(split[i], CultureInfo.InvariantCulture);
                        conf.MarkerWeigths.Add(colName, val);
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
                    var maxDict = new Dictionary<string, double>();


                    for (int i = 1; i < split.Length; i++)
                    {
                        var sourceContrib = double.Parse(split[i], CultureInfo.InvariantCulture);

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

                        used = sourceContrib > 0;

                        if (sourceContrib > 1)
                        {
                            throw new InvalidOperationException("Source max contribution cannnot be higher than 1");
                        }
                        maxDict.Add(colNames[i], sourceContrib);
                        dict.Add(colNames[i], used);
                    }

                    conf.SourcesUsage = dict;
                    conf.MaxSourceContribution = maxDict;

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