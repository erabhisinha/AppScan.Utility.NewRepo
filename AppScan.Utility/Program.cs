﻿using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AppScan.Utility.Models;

namespace AppScan.Utility
{
    class Program
    {
        //---Changes as on 09/June/2024
        private static ILogger _logger = null;
        private static LoggerConfiguration _loggerConfiguration = null;
        private string pwd = "Password For SQL DB";
        private string connpwd = "Dummy Pwd for Conn Str";

        static void Main(string[] args)
        {
            _loggerConfiguration = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Console();
            InitializeLogger(args);

            if (args?.Count() == 5)
            {
                string regularExp = args[0];
                string inputDir = args[1];
                string reportPath = args[2];
                string[] fileExtensionsToScan = args[3].Trim().Split(',');
                if (fileExtensionsToScan?.Count() == 0)
                {
                    Console.WriteLine(Constants.WarningConstants.Invalid_File_Extensions);
                    _logger.Warning(Constants.WarningConstants.Invalid_File_Extensions);
                }
                string exclusionListFile = args[4];
                if (!File.Exists(exclusionListFile))
                {
                    Console.WriteLine(Constants.WarningConstants.Incorrect_Exclusion_List_File);
                    _logger.Warning(Constants.WarningConstants.Incorrect_Exclusion_List_File);
                }
                string exclusionListContent = File.ReadAllText(exclusionListFile);
                FalsePositives falsePositiveList = new FalsePositives(exclusionListContent, inputDir, "\t");
                Scan(regularExp, inputDir, reportPath, fileExtensionsToScan, falsePositiveList);
            }
            else
            {
                Console.WriteLine(Constants.WarningConstants.Invalid_Input_Parameter_Counts);
                _logger.Warning(Constants.WarningConstants.Invalid_Input_Parameter_Counts);
                /*
                string reg = @"(pwd(\s)*(=){1}(\s)*(\""|\').*(\""|\'))|(password(\s)*(=){1}(\s)*(\""|\').*(\""|\'))";
                string dirPath = "C:\\Users\\A153823\\source\\repos\\SourceCode\\AppScan.Utility-main";
                string reportPath = "C:\\Users\\A153823\\source\\repos\\Report";
                string[] fileExtensions = new[] { "*.cs", "*.config" };
                string exclusionFile = @"C:\Users\A153823\source\repos\Report\ExclusionList.csv";
                string exclusionListContent = File.ReadAllText(exclusionFile);
                FalsePositives falsePositiveList = new FalsePositives(exclusionListContent, dirPath, "\t");
                Scan(reg, dirPath, reportPath, fileExtensions, falsePositiveList);

                StringBuilder messageBuilder = new StringBuilder("Parameteres were either not found OR were not confirming to the requirement. Please follow below pattern to provide inputs:");
                messageBuilder.AppendLine("Regex (space) Source-Code-Dir-Path-To-Scan (space) Output-Path-For-Report (space) Comma-Separated-File-Extensions-To-Scan");
                _logger.Warning(messageBuilder.ToString());
                Console.WriteLine(messageBuilder.ToString());
                Console.ReadLine();
                */
            }
        }
        private static void InitializeLogger(string[] args)
        {
            if (args?.Count() > 1)
            {
                string loggerFileName = $"appScanLog-{DateTime.Today.ToString("dd-MMM-yyyy")}.txt";
                _loggerConfiguration = _loggerConfiguration.WriteTo.File($"{args[2]}/{loggerFileName}");
            }
            _logger = _loggerConfiguration.CreateLogger();
        }
        static void Scan(string expression, string folderPath, string outputPath, string[] fileExtensionsToScan, FalsePositives exclusions)
        {
            //expression = @"(.*pwd(\s)*(=){1}(\s)*(\""|\').*(\""|\'))|(.*password(\s)*(=){1}(\s)*(\""|\').*(\""|\'))|(.*password(\s)*(\""|\').*(\""|\'))|(.*pwd(\s)*(\""|\').*(\""|\'))";
            //expression = @"(.*password(\s)*(\""|\').*(\""|\'))";
            //string expression1 = @"(pwd(\s)*(=){1}(\s)*(\""|\').*(\""|\'))|(password(\s)*(=){1}(\s)*(\""|\').*(\""|\'))";



            /* (.*pwd(\s)*(=){1}(\s)*(\""|\').*(\""|\')) - Used to find the sensitive information in code based files
             * .* - Zero OR More occurences of any characters
             * pwd - contains term pwd
             * (\s)* - Zero OR more occurences of spaces
             * (=){1} - Exact one Occurence of equal to sign
             * (\s)* - Zero OR more occurences of spaces
             * (\""|\') - Should have a doubt quote "" OR a single quote '
             * .* - Zero OR More occurences of any characters
             *  (\""|\') - Should have a doubt quote "" OR a single quote '
             *  Example: pwd = "My Dummy Password"; pwd= 'Any clear text password' etc.
             */

            /* (.*password(\s)*(=){1}(\s)*(\""|\').*(\""|\')) - Used to find the sensitive information in code OR config files 
             * .* - Zero OR More occurences of any characters
             * password - contains term pwd
             * (\s)* - Zero OR more occurences of spaces
             * (=){1} - Exact one Occurence of equal to sign
             * (\s)* - Zero OR more occurences of spaces
             * (\""|\') - Should have a doubt quote "" OR a single quote '
             * .* - Zero OR More occurences of any characters
             *  (\""|\') - Should have a doubt quote "" OR a single quote '
             *  Example: password = "My Dummy Password"; password= 'Any clear text password' etc.
             */

            /* (.*password(\s)*(\""|\').*(\""|\')) - Used to find the sensitive information in configuration files
             * .* - Zero OR More occurences of any characters
             * password - contains term pwd
             * (\s)* - Zero OR more occurences of spaces
             * (\""|\') - Should have a doubt quote "" OR a single quote '
             * .* - Zero OR More occurences of any characters
             *  (\""|\') - Should have a doubt quote "" OR a single quote '
             *  Example: <add key="dummyUserPwd" value="MyClearTextPassword"/>
             */

            Regex pwdReg = new Regex(expression, RegexOptions.IgnoreCase);
            DirectoryInfo directoryInfo = new DirectoryInfo(folderPath);
            List<SensitiveContentInfo> sensitiveContents = new List<SensitiveContentInfo>();
            foreach (string pattern in fileExtensionsToScan)
            {
                FileInfo[] filesToScan = directoryInfo.GetFiles(pattern, SearchOption.AllDirectories);
                foreach (FileInfo fileInfo in filesToScan)
                {
                    _logger.Information("Scanning File {@fileName}", fileInfo.Name);
                    string[] fileLines = File.ReadAllLines(fileInfo.FullName);
                    string fileText = File.ReadAllText(fileInfo.FullName);
                    bool isKeyWordIdentified = pwdReg.IsMatch(fileText);
                    if (isKeyWordIdentified)
                    {
                        MatchCollection allMatches = pwdReg.Matches(fileText);
                        _logger.Information("{@fileName} {@noOfOccurencesIdentified}", fileInfo.Name, allMatches.Count);
                        foreach (Match match in allMatches)
                        {
                            int lineNo = LineFromPos(fileText, match.Index);

                            if (lineNo > 0 && !exclusions.Any(x => x.FilePath.Equals(fileInfo.FullName, StringComparison.OrdinalIgnoreCase) && x.LineNo == lineNo))
                            {
                                sensitiveContents.Add(new SensitiveContentInfo
                                {
                                    FileName = fileInfo.FullName,
                                    LineNumber = lineNo,
                                    LineContent = fileLines[lineNo - 1],
                                    KeywordIdentified = match.Value
                                });
                            }
                        }
                    }
                }
            }
            var reportBuilder = new StringBuilder();
            reportBuilder.AppendLine($"File Name\tLine Number\tLine Content\tKeyword Identified");
            _logger.Information("List of all keywords identified are:");
            foreach (var info in sensitiveContents)
            {
                reportBuilder.AppendLine(info.ToCSV());
                _logger.Information("{@object}", info);
            }
            File.WriteAllText($"{outputPath}\\Report-{DateTime.Now.ToString("dd-MMM-yyyy-hh-mm-ss-tt")}.csv", reportBuilder.ToString());
            Console.ReadLine();
        }
        private static int LineFromPos(string input, int indexPosition)
        {
            int lineNumber = 1;
            for (int i = 0; i < indexPosition; i++)
            {
                if (input[i] == '\n') lineNumber++;
            }
            return lineNumber;
        }
    }
}
