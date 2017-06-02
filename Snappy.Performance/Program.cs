﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

using CommandLine;

using Snappy.Sharp;

namespace Snappy.Performance
{
    class Program
    {
        private class CompressionResult
        {
            private static readonly long NanosecPerTick = 1000000000 / Stopwatch.Frequency;
            public CompressionDirection Direction { get; set; }
            public string FileName { get; set; }
            public long FileBytes { get; set; }
            public TimeSpan ElapsedTime { get; set; }
            public double StandardDeviation { get; set; }
            public int Iterations { get; set; }
            public long CompressedSize { get; set; }

            public double CompresionPercentage
            {
                get { return ((double)CompressedSize/FileBytes); }
            }

            public double Throughput
            {
                get
                {
                    double ticksPerIter = (double)ElapsedTime.Ticks / Iterations;
                    double sec = (ticksPerIter * NanosecPerTick) / 1e9;
                    var mb = ((double)FileBytes / (1024 * 1024));
                    return mb/sec;
                }
            }

            public override string ToString()
            {
                return string.Format("{0,-20}\t{1,10}\t{2}\t{3:F2}\t{4:P}\t{5:F2}",
                                     Path.GetFileName(FileName),
                                     ElapsedTime.Ticks * NanosecPerTick,
                                     Iterations,
                                     Throughput,
                                     CompresionPercentage,
                                     StandardDeviation);
            }

            public static string HeaderString = string.Format("{0,-20}\t{1,10}\t{2}\t{3}\t{4}", "File", "Time (ns)", "Iter", "MB/s", "Compression");

            public XElement ToXml()
            {
                var x = new XElement("Result",
                    new XElement("FileName", Path.GetFileName(FileName)),
                    new XElement("FileBytes", FileBytes),
                    new XElement("CompressedSize", CompressedSize),
                    new XElement("Ticks", ElapsedTime.Ticks),
                    new XElement("Iterations", Iterations)
                );
                x.SetAttributeValue("direction", Direction);
                return x;
            }
            public static CompressionResult FromXml(XElement xml)
            {
                return new CompressionResult
                       {
                            FileName = xml.Element("FileName").Value,
                            FileBytes = long.Parse(xml.Element("FileBytes").Value),
                            CompressedSize= long.Parse(xml.Element("CompressedSize").Value),
                            ElapsedTime = new TimeSpan(long.Parse(xml.Element("Ticks").Value)),
                            Iterations = int.Parse(xml.Element("Iterations").Value),
                       };
            }
        }
        static double StdDev(IEnumerable<long> values)
        {
            double ret = 0;
            if (values.Any())
            {
                double avg = values.Average();
                double sum = values.Sum(val => Math.Pow(val - avg, 2));
                ret = Math.Sqrt((sum) / (values.Count() - 1));
            }
            return ret;
        }

        static CompressionResult RunCompression(string fileName, int iterations)
        {
            int size = 0;

            long[] ticks = new long[iterations];
            byte[] uncompressed = File.ReadAllBytes(fileName);

            var target = new SnappyCompressor();
            var s = new Stopwatch();

            for (int i = 0; i < iterations; i++)
            {
                var result = new byte[target.MaxCompressedLength(uncompressed.Length)];
                s.Start();
                size = target.Compress(uncompressed, 0, uncompressed.Length, result);
                s.Stop();
                ticks[i] = s.ElapsedTicks;
                s.Reset();
            }

            return new CompressionResult
                       {
                           Direction = CompressionDirection.Compress,
                           FileName =  FileMap[Path.GetFileName(fileName)],
                           CompressedSize = size,
                           FileBytes = uncompressed.Length,
                           ElapsedTime = new TimeSpan(ticks.Sum()),
                           StandardDeviation =  StdDev(ticks),
                           Iterations = iterations
                       };
        }



        private static CompressionResult RunDecompression(string fileName, int iterations)
        {
            long[] ticks = new long[iterations];
            byte[] uncompressed = File.ReadAllBytes(fileName);
            var compressed = Sharp.Snappy.Compress(uncompressed);
            int size = compressed.Length;
            var target = new SnappyDecompressor();
            var s = new Stopwatch();

            for (int i = 0; i < iterations; i++)
            {
                s.Start();
                var result = target.Decompress(compressed, 0, compressed.Length);
                s.Stop();
                ticks[i] = s.ElapsedTicks;
                s.Reset();
            }

            return new CompressionResult
                       {
                           Direction = CompressionDirection.Decompress,
                           FileName =  FileMap[Path.GetFileName(fileName)],
                           CompressedSize = size,
                           FileBytes = uncompressed.Length,
                           ElapsedTime = new TimeSpan(ticks.Sum()),
                           StandardDeviation =  StdDev(ticks),
                           Iterations = iterations
                       };
        }

        private class Options
        {
            [Option('p', "pref", Default = false, HelpText = "Run performance.")]
            public bool Performance { get; set; }

            [Option('i', "iter", Default = 100, HelpText = "Number of iterations to run.")]
            public int Iterations { get; set; }

            [Option('c', "compare", Default = false, HelpText = "Compare to previous results.")]
            public bool Compare { get; set; }

            [Option('v', "verify", Default = false, HelpText = "Run readfile => compress => decompress => compare results")]
            public bool Verify { get; set; }

            [Option('x', "outputxml", Default = false, HelpText = "Save results to xml file.")]
            public bool WriteXml { get; set; }

            [Option('o', "xmldirectory", HelpText = "Directory to load/store xml results.", Default = @"..\..\..\..\perfdata")] // TODO Path.combine
            public string XmlDirectory { get; set; }

            [Option('d', "datadirectory", HelpText = "Directory to load test files.", Default = @"..\..\..\..\testdata")]
            public string TestDataDirectory { get; set; }

//            [HelpOption]
//            public string GetUsage()
//            {
//                var usage = new StringBuilder();
//                usage.AppendLine("Snappy.Performance.exe");
//                usage.AppendLine("\t-c or --compare to indicate comparision to previous results");
//                usage.AppendLine("\t-v or --verify to indicate data verficiation of round trip");
//                usage.AppendLine("\t-x or --outputxml to indicate save output to xml");
//                usage.AppendLine("\t-d<directory> or --datadirectory<directory> specifies source directory for files to test");
//                usage.AppendLine("\t-o<directory> or --xmldirectory<directory> specifies directory to save xml results");
//                return usage.ToString();
//            }
        }

        static string xmlPath = @"snappyoutput";
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(options =>
                {
                    int iters = options.Iterations;
                    if (Directory.Exists(options.XmlDirectory))
                        xmlPath = options.XmlDirectory;
                    else
                    {
                        throw new DirectoryNotFoundException("Could not find specified xml directory.");
                    }

                    List<CompressionResult> results = new List<CompressionResult>();
                    foreach (string fileName in FileMap.Keys.Select(file => Path.Combine(options.TestDataDirectory,
                        file)))
                    {
                        if (options.Verify)
                        {
                            VerifyRoundTrip(fileName);
                            Console.WriteLine("Verified {0}", Path.GetFileName(fileName));
                        }
                        if (options.Performance)
                        {
                            results.Add(RunCompression(fileName, iters));
                            results.Add(RunDecompression(fileName, iters));
                        }
                    }


                    if (options.Compare)
                    {
                        Console.WriteLine(CompressionResult.HeaderString);
                        CompareResults(results.Where(r => r.Direction == CompressionDirection.Decompress));
                        Console.WriteLine();
                        CompareResults(results.Where(r => r.Direction == CompressionDirection.Compress));

                    }
                    else
                    {
                        foreach (var result in results.Where(r => r.Direction == CompressionDirection.Decompress))
                            Console.WriteLine(result);

                        foreach (var result in results.Where(r => r.Direction == CompressionDirection.Compress))
                            Console.WriteLine(result);
                    }
                    if (options.WriteXml)
                    {
                        WriteResultsAsXml(results);
                    }
                });
        }

        private static void VerifyRoundTrip(string fileName)
        {
            int size = 0;
            byte[] uncompressed = File.ReadAllBytes(fileName);
            var compressor = new SnappyCompressor();
            var result = new byte[compressor.MaxCompressedLength(uncompressed.Length)];
            size = compressor.Compress(uncompressed, 0, uncompressed.Length, result);
            Array.Resize(ref result, size);

            var decompressor = new SnappyDecompressor();
            var decompressed = decompressor.Decompress(result, 0, size);

            byte[] source = File.ReadAllBytes(fileName);
            if (source.Length != decompressed.Length)
                throw new Exception(string.Format("Decompressed length {0} does not match original {1}", decompressed.Length, source.Length));
            for (int i = 0; i < uncompressed.Length; i++)
                if (source[i] != decompressed[i])
                    throw new Exception(string.Format("Decompressed data did not match original at index {0}", i));
        }

        private static void WriteResultsAsXml(IEnumerable<CompressionResult> results)
        {
            XDocument xd = new XDocument();
            xd.Add(new XElement("results", results.Select(r => r.ToXml())));
            using (var file = new FileStream(Path.Combine(xmlPath, string.Format("{0:MMddyyy-hhmmss}.xml", DateTime.Now)), FileMode.CreateNew, FileAccess.Write))
            using (var writer = XmlWriter.Create(file))
            {
                xd.WriteTo(writer);
            }
        }

        private static void CompareResults(IEnumerable<CompressionResult> results)
        {
            var lastResult = Directory.GetFiles(xmlPath, "*.xml", SearchOption.TopDirectoryOnly).Select(f => new {FilePath = f, Creation = File.GetCreationTime(f)}).OrderByDescending(f => f.Creation).FirstOrDefault();

            if (lastResult != null)
            {
                XDocument oldResults;
                using (var file = new FileStream(lastResult.FilePath, FileMode.Open, FileAccess.Read))
                {
                    oldResults = XDocument.Load(file);
                }
                foreach (var r in results)
                {
                    var match = CompressionResult.FromXml(oldResults.Descendants("Result").FirstOrDefault(x => x.Attribute("direction").Value == r.Direction.ToString() && x.Element("FileName").Value == r.FileName));

                    if (match != null)
                    {
                        Console.Write(r.ToString());
                        var currentColor = Console.ForegroundColor;
                        double speedup = CalculateSpeedup(r.Throughput, match.Throughput);
                        if (speedup > 1)
                            Console.ForegroundColor = r.Throughput < match.Throughput ? ConsoleColor.Red : ConsoleColor.Green;
                        Console.WriteLine(" [{0:F2}%]", speedup);
                        Console.ForegroundColor = currentColor;
                    }
                }
            }
        }

        private static double CalculateSpeedup(double throughput, double d)
        {
            return Math.Abs(100 - (100 * throughput / d));
        }

        static readonly Dictionary<string,string> FileMap = new Dictionary<string,string> {
            { "html", "html" },
            { "urls.10K", "urls" },
            { "house.jpg", "jpg"  },
            { "mapreduce-osdi-1.pdf","pdf" },
            { "html_x_4", "html4" },
            { "cp.html", "cp" },
            { "fields.c", "c" },
            { "grammar.lsp", "lsp" },
            { "kennedy.xls", "xls" },
            { "alice29.txt", "txt1" },
            { "asyoulik.txt", "txt2" },
            { "lcet10.txt", "txt3" },
            { "plrabn12.txt", "txt4" },
            { "ptt5", "bin"},
            { "sum", "sum" },
            { "xargs.1", "man" },
            { "geo.protodata","pb"  },
            { "kppkn.gtb","gaviota"  },
        };
    }

    enum CompressionDirection
    {
        Compress,
        Decompress
    }

}
