using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Numerics;

using CommandLine.Options;

namespace ModelMassage
{
    class Program
    {
        private enum Alignment
        {
            None,
            Center,
            BottomCenter
        }

        private class Vertex
        {
            public Vector4 Position;
            public int Line;
        }

        private const string vPattern = @"(?:v)[ \t]+((?:[-+]?\d+(?:\.\d*)?)(?:[eE][-+]\d+)?)\s+((?:[-+]?\d+(?:\.\d*)?)(?:[eE][-+]\d+)?)\s+((?:[-+]?\d+(?:\.\d*)?)(?:[eE][-+]\d+)?)(?:\s+((?:[-+]?\d+(?:\.\d*)?)(?:[eE][-+]\d+)?))?";
        private OptionSet opt;
        private float scaleFactor = 1.0f;
        private Alignment alignment = Alignment.None;
        private string outputPath;
        private bool verbose = false;
        private bool convertInPlace = false;

        private int Run(string[] args)
        {
            bool showHelp = false;

            opt = new OptionSet()
            {
                {"h|?|help", "show this message", v => showHelp = true },
                {"v|verbose", $"enable maximum verbosity [{verbose}]", v => verbose = v != null },
                {"inplace", $"convert in place by overwriting the input file [{convertInPlace}]", v => convertInPlace = v != null },
                {"scale=", $"apply a scale factor {{scale}} [{scaleFactor}]", v => scaleFactor = float.Parse(v) },
                {"align=", $"origin alignment {{align}} [{alignment}]", v => alignment = Enum.Parse<Alignment>(v) },
                {"o=|output=", "output path (ignored if using inplace conversion)", v => outputPath = v },
            };

            try
            {
                var files = opt.Parse(args);

                if (showHelp)
                {
                    WriteHelp();
                    return 0;
                }

                // check source file existance
                foreach (var file in files)
                {
                    if (!File.Exists(file))
                    {
                        throw new FileNotFoundException($"error finding source: {file}");
                    }
                }

                // validate input
                if (!convertInPlace && string.IsNullOrWhiteSpace(outputPath))
                {
                    throw new Exception("you must specify an output path when not converting in place");
                }

                if (scaleFactor <= 0.0f)
                {
                    throw new ArgumentOutOfRangeException($"invalid scale factor: {scaleFactor}");
                }

                // ensure output director exists
                if (!convertInPlace)
                {
                    Directory.CreateDirectory(outputPath);
                }

                // convert all files asyc
                Task.WhenAll(files.Select(f => ConvertModelAsync(f))).Wait();
                
                return 0;
            }
            catch (Exception ex)
            {
                WriteError(ex);
                return 1;
            }
        }

        private async Task ConvertModelAsync(string file)
        {
            var ext = Path.GetExtension(file);
            if (string.Compare(ext, ".obj", true) != 0)
            {
                throw new Exception($"Unknown model format: {file}");
            }

            // perform conversion
            var lines = await File.ReadAllLinesAsync(file);
            int index = 0;

            var vertexes = new List<Vertex>();

            foreach (var line in lines)
            {
                // quick check
                if (line.Length > 2 && line[0] == 'v' && (line[1] == ' ' || line[1]== '\t'))
                {
                    // longer check
                    var m = Regex.Match(line, vPattern);
                    if (m.Success)
                    {
                        // build vertex list
                        var pos = new Vector4(
                            float.Parse(m.Groups[1].Value),
                            float.Parse(m.Groups[2].Value),
                            float.Parse(m.Groups[3].Value),
                            1.0f
                            );

                        vertexes.Add(new Vertex
                        {
                            Position = pos,
                            Line = index
                        });
                    }
                    else
                    {
                        throw new FormatException($"invalid vertex format (line {index + 1}): {line}");
                    }
                }
                index++;
            }

            // perform scaling
            foreach (var vertex in vertexes)
            {
                vertex.Position *= scaleFactor;
                vertex.Position.W = 1.0f;
            }

            if (alignment != Alignment.None)
            {
                // find min-max
                Vector4 boxMin = Vector4.One * float.MaxValue;
                Vector4 boxMax = Vector4.One * float.MinValue;

                foreach (var vertex in vertexes)
                {
                    boxMax.X = Math.Max(boxMax.X, vertex.Position.X);
                    boxMax.Y = Math.Max(boxMax.Y, vertex.Position.Y);
                    boxMax.Z = Math.Max(boxMax.Z, vertex.Position.Z);
                    boxMax.W = Math.Max(boxMax.W, vertex.Position.W);

                    boxMin.X = Math.Min(boxMin.X, vertex.Position.X);
                    boxMin.Y = Math.Min(boxMin.Y, vertex.Position.Y);
                    boxMin.Z = Math.Min(boxMin.Z, vertex.Position.Z);
                    boxMin.W = Math.Min(boxMin.W, vertex.Position.W);
                }

                // perform alignment
                var offset = Vector4.Zero;

                // assume +Z is up (TODO: specify up in arguments)
                switch (alignment)
                {
                    case Alignment.BottomCenter:
                        offset.X = -(boxMax.X - boxMin.X) * 0.5f;
                        offset.Y = -(boxMax.Y - boxMin.Y) * 0.5f;
                        offset.Z = -boxMin.Z;
                        break;
                }

                foreach (var vertex in vertexes)
                {
                    vertex.Position += offset;
                }
            }

            // update lines
            foreach (var vertex in vertexes)
            {
                lines[vertex.Line] = $"v {vertex.Position.X} {vertex.Position.Y} {vertex.Position.Z}";
            }

            var contents = lines.Aggregate((a, b) => a + "\n" + b);

            // write lines
            var outFile = convertInPlace ? file : Path.Combine(outputPath, Path.GetFileName(file));
            await File.WriteAllTextAsync(outFile, contents);
        }

        private void WriteHelp()
        {
            Console.WriteLine("ModelMassage [options] file [files...]\nOptimizes model files for further processing\n");
            opt.WriteOptionDescriptions(Console.Out);

            Console.WriteLine("\narguments after -- are passed to any specified plugins");
        }

        private void WriteError(Exception ex)
        {
            Console.WriteLine(ex.Message);
            if (ex.InnerException != null)
            {
                Console.WriteLine("\t" + ex.InnerException.Message);
            }
            Console.WriteLine($"\nTry `ModelMassage --help' for more information.");
        }

        static int Main(string[] args)
        {
            return new Program().Run(args);
        }
    }
}
