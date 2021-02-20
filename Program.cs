using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using GlobExpressions;

namespace CLRMetaDiff
{
    class Program
    {
        enum ItemChange
        {
            AddedType,
            ModifiedType,
            RemovedType,
            AddedMember,
            RemovedMember,
        }

        static void CompareMembers<MemberType, Comparer>(
            IDictionary<string, ItemChange> diff, 
            IList<MemberType> originalMembers, 
            IList<MemberType> changedMembers, 
            Comparer comparer, 
            string prefix)
            where MemberType : IMemberDef
            where Comparer : IEqualityComparer<MemberType>
        {
            foreach (var member in originalMembers.Except(changedMembers, comparer))
            {
                diff.Add(prefix + member.FullName, ItemChange.RemovedMember);
            }

            foreach (var method in changedMembers.Except(originalMembers, comparer))
            {
                diff.Add(prefix + method.FullName, ItemChange.AddedMember);
            }
        }

        static Dictionary<string, ItemChange> CompareFiles(string originalFile, string changedFile)
        {
            Console.WriteLine($"{Path.GetFileName(originalFile)}");

            var originAr = new AssemblyResolver();
            originAr.PreSearchPaths.Add(Path.GetDirectoryName(originalFile));
            var originModule = ModuleDefMD.Load(originalFile, new ModuleContext(originAr));

            var changedAr = new AssemblyResolver();
            changedAr.PreSearchPaths.Add(Path.GetDirectoryName(changedFile));
            var changedModule = ModuleDefMD.Load(changedFile, new ModuleContext(changedAr));

            Dictionary<string, ItemChange> diff = new Dictionary<string, ItemChange>();        

            foreach (var originalType in originModule.Types)
            {
                if (changedModule.FindNormal(originalType.FullName) is TypeDef changedType)
                {
                    var typeDiff = new Dictionary<string, ItemChange>();

                    CompareMembers(typeDiff, originalType.Methods, changedType.Methods, MethodEqualityComparer.CompareDeclaringTypes, "M:");
                    CompareMembers(typeDiff, originalType.Fields, changedType.Fields, FieldEqualityComparer.CompareDeclaringTypes, "F:");
                    CompareMembers(typeDiff, originalType.Properties, changedType.Properties, PropertyEqualityComparer.CompareDeclaringTypes, "P:");
                    CompareMembers(typeDiff, originalType.Events, changedType.Events, EventEqualityComparer.CompareDeclaringTypes, "E:");

                    if (typeDiff.Count > 0)
                    {
                        diff.Add(originalType.FullName, ItemChange.ModifiedType);

                        foreach (var change in typeDiff)
                            diff.Add(change.Key, change.Value);
                    }
                }
                else
                {
                    diff.Add(originalType.FullName, ItemChange.RemovedType);
                }
            }

            foreach (var type in changedModule.Types.Except(originModule.Types, TypeEqualityComparer.Instance))
            {
                diff.Add(type.FullName, ItemChange.AddedType);
            }

            foreach (var item in diff)
            {
                switch (item.Value)
                {
                    case ItemChange.AddedType:
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write("   +");
                        break;
                    case ItemChange.ModifiedType:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write("   *");
                        break;
                    case ItemChange.RemovedType:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write("   -");
                        break;
                    case ItemChange.AddedMember:
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write("     +");
                        break;
                    case ItemChange.RemovedMember:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write("     -");
                        break;
                }

                Console.Write($"{item.Key}\n");
                Console.ForegroundColor = ConsoleColor.White;
            }

            return diff;
        }

        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Not enough args! Need an original file/dir and a changed file/dir.");
                return;
            }

            string originalPath;
            string changedPath;
            try
            {
                originalPath = Path.GetFullPath(args[0]);
                changedPath = Path.GetFullPath(args[1]);
            }
            catch (Exception ex) when (
                ex is ArgumentException || 
                ex is PathTooLongException || 
                ex is NotSupportedException)
            {
                Console.WriteLine($"Exception parsing paths: {ex}");
                return;
            }

            bool originalPathIsDir = File.GetAttributes(originalPath).HasFlag(System.IO.FileAttributes.Directory);
            bool changedPathIsDir = File.GetAttributes(changedPath).HasFlag(System.IO.FileAttributes.Directory);

            if (originalPathIsDir ^ changedPathIsDir)
            {
                Console.WriteLine("Argument error: paths must be either both directories or both files");
                return;
            }

            if (originalPathIsDir)
            {
                var originalFiles = Glob.Files(originalPath, "*.dll", new GlobOptions() {  });
                var changedFiles = Glob.Files(changedPath, "*.dll");

                var diffs = new List<IDictionary<string, ItemChange>>();
                var overview = new Dictionary<string, ConsoleColor>();

                foreach (var originalFile in originalFiles)
                {
                    if (changedFiles.Contains(originalFile))
                    {
                        diffs.Add(CompareFiles(Path.Combine(originalPath, originalFile), Path.Combine(changedPath, originalFile)));
                    }
                    else
                    {
                        overview.TryAdd($"{Path.GetFileName(originalFile)} is missing from the changed files!", ConsoleColor.Magenta);
                    }
                }

                foreach (var addedFile in changedFiles.Except(originalFiles))
                {
                    overview.TryAdd($"{Path.GetFileName(addedFile)} is missing from the original files!", ConsoleColor.Magenta);
                }

                int numFiles = 0;
                int addedTypes = 0;
                int modifiedTypes = 0;
                int removedTypes = 0;
                int addedMembers = 0;
                int removedMembers = 0;

                foreach (var diff in diffs)
                {
                    numFiles++;

                    foreach (var item in diff)
                    {
                        switch (item.Value)
                        {
                            case ItemChange.AddedType:
                                addedTypes++;
                                break;
                            case ItemChange.ModifiedType:
                                modifiedTypes++;
                                break;
                            case ItemChange.RemovedType:
                                removedTypes++;
                                break;
                            case ItemChange.AddedMember:
                                addedMembers++;
                                break;
                            case ItemChange.RemovedMember:
                                removedMembers++;
                                break;
                        }
                    }
                }

                Console.WriteLine("\nDiff complete!");

                if (overview.Count > 0)
                {
                    Console.WriteLine("\nWarnings:");
                }

                overview.TryAdd($"\nProcessed {numFiles} assemblies with", ConsoleColor.White);
                overview.TryAdd($"  {addedTypes} new types", ConsoleColor.Green);
                overview.TryAdd($"  {removedTypes} deleted types", ConsoleColor.Red);
                overview.TryAdd($"  {modifiedTypes} modified types containing", ConsoleColor.Yellow);
                overview.TryAdd($"    {addedMembers} new members", ConsoleColor.Green);
                overview.TryAdd($"    {removedMembers} deleted members", ConsoleColor.Red);

                foreach (var line in overview)
                {
                    Console.ForegroundColor = line.Value;
                    Console.WriteLine(line.Key);
                }

                Console.ForegroundColor = ConsoleColor.White;
            }
            else
            {
                CompareFiles(originalPath, changedPath);
            }
        }
    }
}
