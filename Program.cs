using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace modpack
{
    public partial class OverrideFile
    {
        [JsonPropertyName("path")]
        public required string Path { get; set; }

        [JsonPropertyName("hash")]
        public required string Hash { get; set; }
    }

    public partial class Addons
    {
        [JsonPropertyName("id")]
        public required string ID { get; set; }

        [JsonPropertyName("version")]
        public required string Version { get; set; }
    }

    public partial class Modpack
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("author")]
        public required string Author { get; set; }

        [JsonPropertyName("version")]
        public required string Version { get; set; }

        [JsonPropertyName("description")]
        public required string Description { get; set; }

        [JsonPropertyName("update")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Update { get; set; }

        [JsonPropertyName("fileApi")]
        public required string FileApi { get; set; }

        [JsonPropertyName("files")]
        public required List<OverrideFile> Files { get; set; }

        [JsonPropertyName("addons")]
        public required List<Addons> Addons { get; set; }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(Modpack))]
    [JsonSerializable(typeof(OverrideFile))]
    [JsonSerializable(typeof(Addons))]
    internal partial class SourceGenerationContext : JsonSerializerContext
    {
    }


    public class FileTree : IDisposable
    {
        private bool isDisposed = false;
        public DirectoryInfo? DirectoryInfo { get; set; }
        public List<FileInfo> FileInfos { get; set; }
        public List<FileTree> ChildDirectory { get; set; }
        public FileTree()
        {
            DirectoryInfo = null;
            FileInfos = [];
            ChildDirectory = [];
        }
        public FileTree(string path)
        {
            DirectoryInfo = new DirectoryInfo(path);
            FileInfos = [..DirectoryInfo.GetFiles()];
            ChildDirectory = [];
            foreach (var Directory in DirectoryInfo.GetDirectories())
            {
                ChildDirectory.Add(new FileTree(Directory.FullName));
            }
        }
        public List<FileInfo> GetAllFileInfos(FileTree? fileTree = null)
        {
            fileTree ??= this;
            List<FileInfo> fileInfos = fileTree.FileInfos;
            foreach (FileTree ChildDirectory in fileTree.ChildDirectory)
            {
                fileInfos.AddRange(GetAllFileInfos(ChildDirectory));
            }
            return fileInfos;
        }
        public List<DirectoryInfo> GetAllDirectoryInfos(FileTree? fileTree = null)
        {
            fileTree ??= this;
            List<DirectoryInfo> DirectoryInfos = [];
            if (fileTree.DirectoryInfo != null){
                DirectoryInfos.Add(fileTree.DirectoryInfo);}
            foreach (FileTree ChildDirectory in fileTree.ChildDirectory)
            {
                DirectoryInfos.AddRange(GetAllDirectoryInfos(ChildDirectory));
            }
            return DirectoryInfos;
        }
        class FileInfoComparer : IEqualityComparer<FileInfo>
        {
            public bool Equals(FileInfo? x, FileInfo? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x is null || y is null) return false;
                return x.LastWriteTime == y.LastWriteTime &&
                       x.CreationTime == y.CreationTime &&
                       x.Length == y.Length &&
                       x.FullName == y.FullName;
            }

            public int GetHashCode(FileInfo obj)
            {
                if (obj is null) return 0;
                int hashLastWriteTime = obj.LastWriteTime.GetHashCode();
                int hashCreationTime = obj.CreationTime.GetHashCode();
                int hashLength = obj.Length.GetHashCode();
                int hashFullName = obj.FullName.GetHashCode();

                return hashLastWriteTime ^ hashCreationTime ^ hashLength ^ hashFullName;
            }
        }

        public List<FileInfo> Intersect(FileTree other)
        {
            var thisFiles = new HashSet<FileInfo>(GetAllFileInfos(), new FileInfoComparer());
            var otherFiles = new HashSet<FileInfo>(other.GetAllFileInfos(), new FileInfoComparer());
            thisFiles.IntersectWith(otherFiles);
            return [.. thisFiles];
        }

        public List<FileInfo> Union(FileTree other)
        {
            var thisFiles = new HashSet<FileInfo>(GetAllFileInfos(), new FileInfoComparer());
            var otherFiles = new HashSet<FileInfo>(other.GetAllFileInfos(), new FileInfoComparer());
            thisFiles.UnionWith(otherFiles);
            return [.. thisFiles];
        }

        public List<FileInfo> Difference(FileTree other)
        {
            var thisFiles = new HashSet<FileInfo>(GetAllFileInfos(), new FileInfoComparer());
            var otherFiles = new HashSet<FileInfo>(other.GetAllFileInfos(), new FileInfoComparer());
            thisFiles.ExceptWith(otherFiles);
            return [.. thisFiles];
        }

        public List<FileInfo> Complement(FileTree other)
        {
            var thisFiles = new HashSet<FileInfo>(GetAllFileInfos(), new FileInfoComparer());
            var otherFiles = new HashSet<FileInfo>(other.GetAllFileInfos(), new FileInfoComparer());
            otherFiles.ExceptWith(thisFiles);
            return [.. otherFiles];
        }

        public void Close()
        {
            Dispose(true);
        }
        ~FileTree()
        {
            Dispose(false);
        }
        void IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    foreach (FileTree ChildDirectory in ChildDirectory)
                    {
                        ChildDirectory.Close();
                    }
                }
            }
            isDisposed = true;
        }
    }
    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && Directory.Exists(args[0]))
                {
                    DirectoryInfo modpackPath = new(args[0]);
                    DirectoryInfo overridesPath = new(Path.Combine(modpackPath.FullName, "overrides"));
                    FileInfo manifestPath = new(Path.Combine(modpackPath.FullName, "server-manifest.json"));
                    FileInfo FileInfoCachePath = new(Path.Combine(modpackPath.FullName, "fileCache.json"));

                    Modpack modpack = JsonSerializer.Deserialize(File.ReadAllText(manifestPath.FullName), typeof(Modpack), new JsonSerializerOptions { TypeInfoResolver = SourceGenerationContext.Default }) as Modpack ?? throw new Exception("Deserialize modpack Failed!");

                    Console.Out.WriteLine($"Creating modpack...");

                    Dictionary<string, OverrideFile> modpackFiles = [];

                    Dictionary<string, FileInfo> modpackFileInfos = [];

                    foreach (var file in modpack.Files)
                    {
                        modpackFiles.Add(file.Hash, file);
                        modpackFileInfos.Add(file.Hash, new FileInfo(Path.Combine([overridesPath.FullName, .. file.Path.Split('/')])));
                    }

                    //处理在清单中存在的文件
                    foreach (var file in modpackFileInfos.Where(item => item.Value.Exists))
                    {
                        string fileHash = GetFileHash(file.Value.FullName);
                        if(fileHash != file.Key)
                        {
                            modpackFiles[file.Key].Hash = fileHash;
                        }
                    }
                    //处理在清单中不存在的文件
                    foreach (var file in modpackFileInfos.Where(item => !item.Value.Exists))
                    {
                        modpackFiles.Remove(file.Key);
                        modpackFileInfos.Remove(file.Key);
                    }

                    FileTree fileTree = new(overridesPath.FullName);

                    //处理在清单中不存在但实际目录中存在的文件
                    var addFiles = fileTree.GetAllFileInfos().Where(file => !modpackFileInfos.ContainsValue(file));


                    foreach (var file in addFiles)
                    {
                        string fileHash = GetFileHash(file.FullName);
                        modpackFiles.Add(fileHash, new OverrideFile() { Path = Path.GetRelativePath(overridesPath!.FullName, file.FullName).Replace('\\', '/'), Hash = fileHash });
                    }

                    //将处理后的文件列表覆盖原本的清单
                    modpack.Files = [.. modpackFiles.Values];

                    //保存清单
                    while (IsFileLocked(manifestPath))
                    {
                        Console.Out.WriteLine($"{manifestPath.Name} is being used");
                        Thread.Sleep(1000);
                    }
                    int[] version = [..modpack.Version.Split(".").Select(i => Convert.ToInt32(i))];
                    version[^1] = version[^1] + 1;
                    modpack.Version = string.Join(".", version);
                    File.WriteAllText(manifestPath.FullName, JsonSerializer.Serialize(modpack, typeof(Modpack), new JsonSerializerOptions { TypeInfoResolver = SourceGenerationContext.Default, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

                    Console.Out.WriteLine($"Create modpack completed.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                Console.Out.WriteLine("Press any key to exit.");
                Console.ReadKey(true);
            }
            
        }
        private static bool IsFileLocked(FileInfo file)
        {
            FileStream? stream = null;
            try
            {
                stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return true;
            }
            finally
            {
                stream?.Close();
            }
            return false;
        }

        public static string GetFileHash(string path)
        {
            try
            {
                using FileStream file = new(path, FileMode.Open);
                using SHA1 sha1 = SHA1.Create();
                return BitConverter.ToString(sha1.ComputeHash(file)).Replace("-", string.Empty).ToLower();
            }
            catch (IOException ex)
            {
                throw new IOException($"Unable to open file {path}. {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new UnauthorizedAccessException($"No permission to access file {path}. {ex}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get hash of file {path}. {ex}");
            }
        }

    }
}
