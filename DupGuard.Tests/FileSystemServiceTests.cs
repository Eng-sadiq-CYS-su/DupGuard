using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services;

namespace DupGuard.Tests
{
    [TestClass]
    public class FileSystemServiceTests
    {
        private IFileSystemService _fileSystemService;
        private ILogger _logger;

        [TestInitialize]
        public void Setup()
        {
            _logger = new Logger();
            _fileSystemService = new FileSystemService(_logger);
        }

        [TestMethod]
        public void IsSystemFile_WindowsSystemPaths_ReturnsTrue()
        {
            // Arrange
            var systemPaths = new[]
            {
                @"C:\Windows\System32\kernel32.dll",
                @"C:\Program Files\Windows Defender\test.exe",
                @"C:\ProgramData\Microsoft\Windows\test.dat",
                @"C:\System Volume Information\test",
                @"C:\$Recycle.Bin\S-1-5-21\test"
            };

            foreach (var path in systemPaths)
            {
                // Act
                var fileInfo = new Models.FileInfo { FullPath = path };
                var result = _fileSystemService.IsSystemFile(fileInfo);

                // Assert
                Assert.IsTrue(result, $"Path {path} should be identified as system file");
            }
        }

        [TestMethod]
        public void IsSystemFile_UserPaths_ReturnsFalse()
        {
            // Arrange
            var userPaths = new[]
            {
                @"C:\Users\John\Documents\test.docx",
                @"C:\Users\John\Pictures\photo.jpg",
                @"D:\Data\files\test.txt",
                @"\\server\share\file.pdf"
            };

            foreach (var path in userPaths)
            {
                // Act
                var fileInfo = new Models.FileInfo { FullPath = path };
                var result = _fileSystemService.IsSystemFile(fileInfo);

                // Assert
                Assert.IsFalse(result, $"Path {path} should not be identified as system file");
            }
        }

        [TestMethod]
        public void IsExcludedFile_SystemFileWithExclusionEnabled_ReturnsTrue()
        {
            // Arrange
            var fileInfo = new Models.FileInfo
            {
                FullPath = @"C:\Windows\System32\kernel32.dll",
                Size = 1024,
                Attributes = FileAttributes.System
            };

            var options = new ScanOptions
            {
                ExcludeSystemFiles = true,
                MinFileSize = 0
            };

            // Act
            var result = _fileSystemService.IsExcludedFile(fileInfo, options);

            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(fileInfo.IsExcluded);
            Assert.AreEqual("ملف نظام", fileInfo.ExclusionReason);
        }

        [TestMethod]
        public void IsExcludedFile_HiddenFileWithExclusionEnabled_ReturnsTrue()
        {
            // Arrange
            var fileInfo = new Models.FileInfo
            {
                FullPath = @"C:\Users\John\Documents\hidden.txt",
                Size = 1024,
                Attributes = FileAttributes.Hidden
            };

            var options = new ScanOptions
            {
                ExcludeSystemFiles = false,
                ExcludeHiddenFiles = true,
                MinFileSize = 0
            };

            // Act
            var result = _fileSystemService.IsExcludedFile(fileInfo, options);

            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(fileInfo.IsExcluded);
            Assert.AreEqual("ملف مخفي", fileInfo.ExclusionReason);
        }

        [TestMethod]
        public void IsExcludedFile_SmallFileWithMinSizeLimit_ReturnsTrue()
        {
            // Arrange
            var fileInfo = new Models.FileInfo
            {
                FullPath = @"C:\Users\John\Documents\small.txt",
                Size = 512, // 512 bytes, below 1KB minimum
                Attributes = FileAttributes.Normal
            };

            var options = new ScanOptions
            {
                ExcludeSystemFiles = false,
                ExcludeHiddenFiles = false,
                MinFileSize = 1024 // 1KB minimum
            };

            // Act
            var result = _fileSystemService.IsExcludedFile(fileInfo, options);

            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(fileInfo.IsExcluded);
            StringAssert.Contains(fileInfo.ExclusionReason, "حجم الملف خارج النطاق");
        }

        [TestMethod]
        public void IsExcludedFile_ValidFile_ReturnsFalse()
        {
            // Arrange
            var fileInfo = new Models.FileInfo
            {
                FullPath = @"C:\Users\John\Documents\valid.txt",
                Size = 2048, // 2KB, above minimum
                Attributes = FileAttributes.Normal
            };

            var options = new ScanOptions
            {
                ExcludeSystemFiles = true,
                ExcludeHiddenFiles = true,
                MinFileSize = 1024
            };

            // Act
            var result = _fileSystemService.IsExcludedFile(fileInfo, options);

            // Assert
            Assert.IsFalse(result);
            Assert.IsFalse(fileInfo.IsExcluded);
            Assert.IsNull(fileInfo.ExclusionReason);
        }
    }
}
