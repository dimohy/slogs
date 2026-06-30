using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Fsp;
using VolumeInfo = Fsp.Interop.VolumeInfo;
using FspFileInfo = Fsp.Interop.FileInfo;
using IoFileAttributes = System.IO.FileAttributes;

namespace Slogs.Obsidian.Drive;

internal sealed class SlogsObsidianFileSystem : FileSystemBase
{
    private const int AllocationUnit = 4096;
    private static readonly byte[] DefaultSecurityDescriptor = CreateDefaultSecurityDescriptor();
    private readonly string rootPath;
    private readonly SlogsObsidianSyncService syncService;

    public SlogsObsidianFileSystem(string rootPath, SlogsObsidianSyncService syncService)
    {
        this.rootPath = Path.GetFullPath(rootPath);
        this.syncService = syncService;
    }

    public override int ExceptionHandler(Exception ex)
    {
        Console.Error.WriteLine($"WinFsp operation failed: {ex.Message}");
        return MapException(ex);
    }

    public override int GetVolumeInfo(out VolumeInfo volumeInfo)
    {
        volumeInfo = default;
        try
        {
            var usage = syncService.GetVolumeUsage();
            volumeInfo.TotalSize = (ulong)Math.Max(0L, usage.TotalSizeBytes);
            volumeInfo.FreeSize = (ulong)Math.Max(0L, usage.FreeSizeBytes);
            volumeInfo.SetVolumeLabel("Slogs Obsidian");
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public override int GetSecurityByName(string fileName, out uint fileAttributes, ref byte[] securityDescriptor)
    {
        fileAttributes = 0;
        try
        {
            var fullPath = ToFullPath(fileName);
            if (!Exists(fullPath))
            {
                return ParentExists(fullPath) ? STATUS_OBJECT_NAME_NOT_FOUND : STATUS_OBJECT_PATH_NOT_FOUND;
            }

            fileAttributes = (uint)File.GetAttributes(fullPath);
            if (securityDescriptor is not null)
            {
                securityDescriptor = DefaultSecurityDescriptor;
            }

            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public override int Create(
        string fileName,
        uint createOptions,
        uint grantedAccess,
        uint fileAttributes,
        byte[] securityDescriptor,
        ulong allocationSize,
        out object fileNode,
        out object fileDesc,
        out FspFileInfo fileInfo,
        out string normalizedName)
    {
        _ = grantedAccess;
        _ = securityDescriptor;
        _ = allocationSize;
        fileNode = default!;
        fileDesc = default!;
        fileInfo = default;
        normalizedName = fileName;

        try
        {
            var fullPath = ToFullPath(fileName);
            var isDirectory = (createOptions & FILE_DIRECTORY_FILE) != 0;
            if (Exists(fullPath))
            {
                return STATUS_OBJECT_NAME_COLLISION;
            }

            var parent = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            {
                return STATUS_OBJECT_PATH_NOT_FOUND;
            }

            if (isDirectory)
            {
                Directory.CreateDirectory(fullPath);
                fileDesc = DriveFileHandle.ForDirectory(fullPath, ToRelativePath(fullPath));
            }
            else
            {
                var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                if (fileAttributes != 0)
                {
                    File.SetAttributes(fullPath, (IoFileAttributes)fileAttributes | IoFileAttributes.Archive);
                }

                fileDesc = DriveFileHandle.ForFile(fullPath, ToRelativePath(fullPath), stream, dirty: true);
            }

            fileNode = fileDesc;
            fileInfo = BuildFileInfo(fullPath);
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public override int Open(
        string fileName,
        uint createOptions,
        uint grantedAccess,
        out object fileNode,
        out object fileDesc,
        out FspFileInfo fileInfo,
        out string normalizedName)
    {
        _ = grantedAccess;
        fileNode = default!;
        fileDesc = default!;
        fileInfo = default;
        normalizedName = fileName;

        try
        {
            var fullPath = ToFullPath(fileName);
            if (Directory.Exists(fullPath))
            {
                if ((createOptions & FILE_NON_DIRECTORY_FILE) != 0)
                {
                    return STATUS_FILE_IS_A_DIRECTORY;
                }

                fileDesc = DriveFileHandle.ForDirectory(fullPath, ToRelativePath(fullPath));
            }
            else if (File.Exists(fullPath))
            {
                if ((createOptions & FILE_DIRECTORY_FILE) != 0)
                {
                    return STATUS_NOT_A_DIRECTORY;
                }

                var stream = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                fileDesc = DriveFileHandle.ForFile(fullPath, ToRelativePath(fullPath), stream, dirty: false);
            }
            else
            {
                return ParentExists(fullPath) ? STATUS_OBJECT_NAME_NOT_FOUND : STATUS_OBJECT_PATH_NOT_FOUND;
            }

            fileNode = fileDesc;
            fileInfo = BuildFileInfo(fullPath);
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public override int Overwrite(
        object fileNode,
        object fileDesc,
        uint fileAttributes,
        bool replaceFileAttributes,
        ulong allocationSize,
        out FspFileInfo fileInfo)
    {
        _ = fileNode;
        _ = allocationSize;
        fileInfo = default;

        try
        {
            var handle = RequireFileHandle(fileDesc);
            lock (handle)
            {
                handle.Stream!.SetLength(0);
                handle.Dirty = true;
            }

            if (fileAttributes != 0 || replaceFileAttributes)
            {
                File.SetAttributes(
                    handle.FullPath,
                    replaceFileAttributes
                        ? (IoFileAttributes)fileAttributes
                        : File.GetAttributes(handle.FullPath) | (IoFileAttributes)fileAttributes);
            }

            fileInfo = BuildFileInfo(handle.FullPath);
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public override void Cleanup(object fileNode, object fileDesc, string fileName, uint flags)
    {
        _ = fileNode;
        _ = fileName;
        if ((flags & CleanupDelete) == 0 || fileDesc is not DriveFileHandle handle)
        {
            return;
        }

        try
        {
            handle.Stream?.Dispose();
            if (handle.IsDirectory)
            {
                Directory.Delete(handle.FullPath);
            }
            else if (File.Exists(handle.FullPath))
            {
                File.Delete(handle.FullPath);
            }

            handle.Deleted = true;
            syncService.DeleteLocalPathAsync(handle.RelativePath, handle.IsDirectory).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Slogs delete sync failed: {ex.Message}");
        }
    }

    public override void Close(object fileNode, object fileDesc)
    {
        _ = fileNode;
        if (fileDesc is not DriveFileHandle handle)
        {
            return;
        }

        try
        {
            if (!handle.Deleted && !handle.IsDirectory && handle.Dirty)
            {
                handle.Stream?.Flush(true);
                handle.Dispose();
                syncService.FlushLocalFileAsync(handle.RelativePath).GetAwaiter().GetResult();
                handle.Dirty = false;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Slogs close sync failed for '{handle.RelativePath}': {ex.Message}");
        }
        finally
        {
            handle.Dispose();
        }
    }

    public override int Read(
        object fileNode,
        object fileDesc,
        IntPtr buffer,
        ulong offset,
        uint length,
        out uint bytesTransferred)
    {
        _ = fileNode;
        bytesTransferred = 0;

        try
        {
            var handle = RequireFileHandle(fileDesc);
            lock (handle)
            {
                if (offset >= (ulong)handle.Stream!.Length)
                {
                    return STATUS_SUCCESS;
                }

                var bytesToRead = (int)Math.Min(length, (ulong)handle.Stream.Length - offset);
                var bytes = new byte[bytesToRead];
                handle.Stream.Seek((long)offset, SeekOrigin.Begin);
                var read = handle.Stream.Read(bytes, 0, bytes.Length);
                Marshal.Copy(bytes, 0, buffer, read);
                bytesTransferred = (uint)read;
            }

            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public override int Write(
        object fileNode,
        object fileDesc,
        IntPtr buffer,
        ulong offset,
        uint length,
        bool writeToEndOfFile,
        bool constrainedIo,
        out uint bytesTransferred,
        out FspFileInfo fileInfo)
    {
        _ = fileNode;
        bytesTransferred = 0;
        fileInfo = default;

        try
        {
            var handle = RequireFileHandle(fileDesc);
            lock (handle)
            {
                var stream = handle.Stream!;
                if (writeToEndOfFile)
                {
                    offset = (ulong)stream.Length;
                }

                var bytesToWrite = (int)length;
                if (constrainedIo)
                {
                    if (offset >= (ulong)stream.Length)
                    {
                        fileInfo = BuildFileInfo(handle.FullPath);
                        return STATUS_SUCCESS;
                    }

                    bytesToWrite = (int)Math.Min(length, (ulong)stream.Length - offset);
                }

                var bytes = new byte[bytesToWrite];
                Marshal.Copy(buffer, bytes, 0, bytes.Length);
                stream.Seek((long)offset, SeekOrigin.Begin);
                stream.Write(bytes, 0, bytes.Length);
                handle.Dirty = true;
                bytesTransferred = (uint)bytes.Length;
            }

            fileInfo = BuildFileInfo(handle.FullPath);
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public override int Flush(object fileNode, object fileDesc, out FspFileInfo fileInfo)
    {
        _ = fileNode;
        fileInfo = default;
        if (fileDesc is null)
        {
            return STATUS_SUCCESS;
        }

        try
        {
            var handle = RequireFileHandle(fileDesc);
            lock (handle)
            {
                handle.Stream!.Flush(true);
            }

            if (handle.Dirty)
            {
                syncService.FlushLocalFileAsync(handle.RelativePath).GetAwaiter().GetResult();
                handle.Dirty = false;
            }

            fileInfo = BuildFileInfo(handle.FullPath);
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public override int GetFileInfo(object fileNode, object fileDesc, out FspFileInfo fileInfo)
    {
        _ = fileNode;
        fileInfo = default;

        try
        {
            var handle = (DriveFileHandle)fileDesc;
            fileInfo = BuildFileInfo(handle.FullPath);
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public override int SetBasicInfo(
        object fileNode,
        object fileDesc,
        uint fileAttributes,
        ulong creationTime,
        ulong lastAccessTime,
        ulong lastWriteTime,
        ulong changeTime,
        out FspFileInfo fileInfo)
    {
        _ = fileNode;
        _ = changeTime;
        fileInfo = default;

        try
        {
            var handle = (DriveFileHandle)fileDesc;
            if (fileAttributes is not 0 and not uint.MaxValue)
            {
                File.SetAttributes(handle.FullPath, (IoFileAttributes)fileAttributes);
            }

            ApplyTime(handle.FullPath, creationTime, File.SetCreationTimeUtc, Directory.SetCreationTimeUtc);
            ApplyTime(handle.FullPath, lastAccessTime, File.SetLastAccessTimeUtc, Directory.SetLastAccessTimeUtc);
            ApplyTime(handle.FullPath, lastWriteTime, File.SetLastWriteTimeUtc, Directory.SetLastWriteTimeUtc);

            if (!handle.IsDirectory)
            {
                handle.Dirty = true;
            }

            fileInfo = BuildFileInfo(handle.FullPath);
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public override int SetFileSize(
        object fileNode,
        object fileDesc,
        ulong newSize,
        bool setAllocationSize,
        out FspFileInfo fileInfo)
    {
        _ = fileNode;
        fileInfo = default;

        try
        {
            var handle = RequireFileHandle(fileDesc);
            if (!setAllocationSize)
            {
                lock (handle)
                {
                    handle.Stream!.SetLength((long)newSize);
                    handle.Dirty = true;
                }
            }

            fileInfo = BuildFileInfo(handle.FullPath);
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public override int CanDelete(object fileNode, object fileDesc, string fileName)
    {
        _ = fileNode;
        _ = fileName;

        try
        {
            var handle = (DriveFileHandle)fileDesc;
            if (handle.IsDirectory && Directory.EnumerateFileSystemEntries(handle.FullPath).Any())
            {
                return STATUS_DIRECTORY_NOT_EMPTY;
            }

            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public override int Rename(
        object fileNode,
        object fileDesc,
        string fileName,
        string newFileName,
        bool replaceIfExists)
    {
        _ = fileNode;
        _ = fileDesc;

        try
        {
            var source = ToFullPath(fileName);
            var target = ToFullPath(newFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)
                ?? throw new InvalidOperationException($"Invalid target path: {newFileName}"));

            if (Directory.Exists(source))
            {
                if (Exists(target))
                {
                    return replaceIfExists ? STATUS_ACCESS_DENIED : STATUS_OBJECT_NAME_COLLISION;
                }

                Directory.Move(source, target);
            }
            else if (File.Exists(source))
            {
                if (Directory.Exists(target))
                {
                    return STATUS_FILE_IS_A_DIRECTORY;
                }

                File.Move(source, target, replaceIfExists);
            }
            else
            {
                return STATUS_OBJECT_NAME_NOT_FOUND;
            }

            if (fileDesc is DriveFileHandle handle)
            {
                handle.FullPath = target;
                handle.RelativePath = ToRelativePath(target);
            }

            syncService.RenameLocalPathAsync().GetAwaiter().GetResult();
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public override int GetSecurity(object fileNode, object fileDesc, ref byte[] securityDescriptor)
    {
        _ = fileNode;
        _ = fileDesc;
        securityDescriptor = DefaultSecurityDescriptor;
        return STATUS_SUCCESS;
    }

    public override int SetSecurity(
        object fileNode,
        object fileDesc,
        AccessControlSections sections,
        byte[] securityDescriptor)
    {
        _ = fileNode;
        _ = fileDesc;
        _ = sections;
        _ = securityDescriptor;
        return STATUS_SUCCESS;
    }

    public override bool ReadDirectoryEntry(
        object fileNode,
        object fileDesc,
        string pattern,
        string marker,
        ref object context,
        out string fileName,
        out FspFileInfo fileInfo)
    {
        _ = fileNode;
        _ = pattern;
        fileName = default!;
        fileInfo = default;

        try
        {
            var handle = (DriveFileHandle)fileDesc;
            if (!handle.IsDirectory)
            {
                return false;
            }

            if (context is not IEnumerator<DirectoryEntry> enumerator)
            {
                IEnumerable<DirectoryEntry> entries = BuildDirectoryEntries(handle);
                if (!string.IsNullOrEmpty(marker))
                {
                    // WinFsp restarts directory enumeration with Context reset to null and the
                    // last returned name passed as marker. Honor it so directories larger than a
                    // single query buffer resume after the marker instead of repeating entries.
                    entries = entries
                        .Where(entry => entry.Name is not "." and not "..")
                        .Where(entry => string.Compare(entry.Name, marker, StringComparison.OrdinalIgnoreCase) > 0);
                }

                context = enumerator = entries.GetEnumerator();
            }

            while (enumerator.MoveNext())
            {
                var entry = enumerator.Current;
                fileName = entry.Name;
                fileInfo = entry.Info;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Directory enumeration failed: {ex.Message}");
            return false;
        }
    }

    public override int GetDirInfoByName(
        object fileNode,
        object fileDesc,
        string fileName,
        out string normalizedName,
        out FspFileInfo fileInfo)
    {
        _ = fileNode;
        normalizedName = fileName;
        fileInfo = default;

        try
        {
            var handle = (DriveFileHandle)fileDesc;
            var fullPath = fileName switch
            {
                "." => handle.FullPath,
                ".." => Path.GetDirectoryName(handle.FullPath) ?? handle.FullPath,
                _ => Path.Combine(handle.FullPath, fileName)
            };

            if (!Exists(fullPath))
            {
                return STATUS_OBJECT_NAME_NOT_FOUND;
            }

            fileInfo = BuildFileInfo(fullPath);
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private List<DirectoryEntry> BuildDirectoryEntries(DriveFileHandle handle)
    {
        var entries = new List<DirectoryEntry>();
        if (!PathsEqual(handle.FullPath, rootPath))
        {
            entries.Add(new DirectoryEntry(".", BuildFileInfo(handle.FullPath)));
            var parent = Path.GetDirectoryName(handle.FullPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                entries.Add(new DirectoryEntry("..", BuildFileInfo(parent)));
            }
        }

        entries.AddRange(Directory
            .EnumerateFileSystemEntries(handle.FullPath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => new DirectoryEntry(Path.GetFileName(path), BuildFileInfo(path))));
        return entries;
    }

    private FspFileInfo BuildFileInfo(string fullPath)
    {
        var attributes = File.GetAttributes(fullPath);
        var isDirectory = attributes.HasFlag(IoFileAttributes.Directory);
        var fileLength = isDirectory ? 0UL : (ulong)new System.IO.FileInfo(fullPath).Length;
        return new FspFileInfo
        {
            FileAttributes = (uint)attributes,
            ReparseTag = 0,
            FileSize = fileLength,
            AllocationSize = RoundAllocation(fileLength),
            CreationTime = (ulong)(isDirectory ? Directory.GetCreationTimeUtc(fullPath) : File.GetCreationTimeUtc(fullPath)).ToFileTimeUtc(),
            LastAccessTime = (ulong)(isDirectory ? Directory.GetLastAccessTimeUtc(fullPath) : File.GetLastAccessTimeUtc(fullPath)).ToFileTimeUtc(),
            LastWriteTime = (ulong)(isDirectory ? Directory.GetLastWriteTimeUtc(fullPath) : File.GetLastWriteTimeUtc(fullPath)).ToFileTimeUtc(),
            ChangeTime = (ulong)(isDirectory ? Directory.GetLastWriteTimeUtc(fullPath) : File.GetLastWriteTimeUtc(fullPath)).ToFileTimeUtc(),
            IndexNumber = 0,
            HardLinks = 0
        };
    }

    private string ToFullPath(string fileName)
    {
        var relative = fileName.TrimStart('\\').Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relative));
        var rootWithSeparator = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        if (!fullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase)
            && !fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Path escapes Slogs drive root: {fileName}");
        }

        return fullPath;
    }

    private string ToRelativePath(string fullPath)
        => Path.GetRelativePath(rootPath, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    private static DriveFileHandle RequireFileHandle(object fileDesc)
    {
        var handle = (DriveFileHandle)fileDesc;
        if (handle.IsDirectory)
        {
            throw new IOException("Path is a directory.");
        }

        return handle;
    }

    private static void ApplyTime(
        string path,
        ulong fileTime,
        Action<string, DateTime> setFileTime,
        Action<string, DateTime> setDirectoryTime)
    {
        if (fileTime == 0)
        {
            return;
        }

        var value = DateTime.FromFileTimeUtc((long)fileTime);
        if (Directory.Exists(path))
        {
            setDirectoryTime(path, value);
        }
        else
        {
            setFileTime(path, value);
        }
    }

    private static bool Exists(string fullPath)
        => File.Exists(fullPath) || Directory.Exists(fullPath);

    private static bool ParentExists(string fullPath)
    {
        var parent = Path.GetDirectoryName(fullPath);
        return !string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static ulong RoundAllocation(ulong size)
        => ((size + AllocationUnit - 1) / AllocationUnit) * AllocationUnit;

    private static int MapException(Exception ex)
        => ex switch
        {
            UnauthorizedAccessException => STATUS_ACCESS_DENIED,
            DirectoryNotFoundException => STATUS_OBJECT_PATH_NOT_FOUND,
            FileNotFoundException => STATUS_OBJECT_NAME_NOT_FOUND,
            PathTooLongException => STATUS_OBJECT_NAME_INVALID,
            SlogsObsidianSyncConflictException => STATUS_ACCESS_DENIED,
            IOException => STATUS_UNEXPECTED_IO_ERROR,
            _ => STATUS_UNEXPECTED_IO_ERROR
        };

    private static byte[] CreateDefaultSecurityDescriptor()
    {
        var descriptor = new RawSecurityDescriptor("O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;WD)");
        var data = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(data, 0);
        return data;
    }

    private sealed record DirectoryEntry(string Name, FspFileInfo Info);

    private sealed class DriveFileHandle : IDisposable
    {
        private DriveFileHandle(string fullPath, string relativePath, bool isDirectory, FileStream? stream, bool dirty)
        {
            FullPath = fullPath;
            RelativePath = relativePath;
            IsDirectory = isDirectory;
            Stream = stream;
            Dirty = dirty;
        }

        public string FullPath { get; set; }

        public string RelativePath { get; set; }

        public bool IsDirectory { get; }

        public FileStream? Stream { get; }

        public bool Dirty { get; set; }

        public bool Deleted { get; set; }

        public static DriveFileHandle ForDirectory(string fullPath, string relativePath)
            => new(fullPath, relativePath, isDirectory: true, stream: null, dirty: false);

        public static DriveFileHandle ForFile(string fullPath, string relativePath, FileStream stream, bool dirty)
            => new(fullPath, relativePath, isDirectory: false, stream, dirty);

        public void Dispose()
        {
            Stream?.Dispose();
        }
    }
}
