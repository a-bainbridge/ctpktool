using System;
using System.IO;

using CommandLine;
using CommandLine.Text;

namespace ctpktool
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = new Config();

            var settings = new CommandLine.ParserSettings(true, true, false, Console.Error);
            var parser = new CommandLine.Parser(settings);

            string inputPath = null, outputPath = null;
            bool isExtract = false, isRawExtract = false, isCreate = false;
            bool outputInfo = false;

            if(args.Length == 0)
            {
                // Don't try to parse zero arguments or else it results in an exception
                Console.WriteLine(config.GetUsage());
                Environment.Exit(-1);
            }

            if (parser.ParseArguments(args, config))
            {
                outputInfo = config.GenInfo;
                if (!String.IsNullOrWhiteSpace(config.InputFileRaw))
                {
                    inputPath = config.InputFileRaw;
                    isRawExtract = true;
                    isExtract = true;
                }
                else if (!String.IsNullOrWhiteSpace(config.InputFile))
                {
                    inputPath = config.InputFile;
                    isExtract = true;
                }
                else if (!String.IsNullOrWhiteSpace(config.InputFolder))
                {
                    inputPath = config.InputFolder;
                    isCreate = true;
                }

                if (!String.IsNullOrWhiteSpace(config.OutputPath))
                {
                    outputPath = config.OutputPath;
                }
                else
                {
                    if (isCreate)
                    {
                        outputPath = inputPath + ".ctpk";
                    }
                    else
                    {
                        string basePath = Path.GetDirectoryName(inputPath);
                        string baseFilename = Path.GetFileNameWithoutExtension(inputPath);

                        if (!String.IsNullOrWhiteSpace(basePath))
                        {
                            baseFilename = Path.Combine(basePath, baseFilename);
                        }

                        outputPath = baseFilename;
                    }
                }
            }

            if (isCreate)
            {
                Ctpk.Create(inputPath, outputPath);
            }
            else if (isExtract)
            {
                Ctpk.Read(inputPath, outputPath, isRawExtract, outputInfo);
            }
            else
            {
                Console.WriteLine("Could not find path or file '{0}'", args[0]);
            }
        }
    }
}
