using IMCAPI.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Configuration;
using SkiaSharp;

namespace Testing
{
    [TestFixture]
    public class AttachmentStorageTests
    {
        private sealed class TempEnvironment : IWebHostEnvironment, IDisposable
        {
            public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "imc-media-tests", Guid.NewGuid().ToString("N"));
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
            public string EnvironmentName { get; set; } = "Test";
            public string ApplicationName { get; set; } = "IMCAPI";
            public string WebRootPath { get; set; } = string.Empty;
            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

            public void Dispose()
            {
                if (Directory.Exists(ContentRootPath))
                {
                    Directory.Delete(ContentRootPath, recursive: true);
                }
            }
        }

        private static byte[] Photo(int width, int height)
        {
            using var bitmap = new SKBitmap(width, height);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.White);
                using var paint = new SKPaint { Color = SKColors.DarkSlateBlue };
                canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            return data.ToArray();
        }

        [Test]
        public void ThumbnailIsAJpegDataUrlNoLargerThanTheEdge()
        {
            var thumbnail = AttachmentStorage.CreateThumbnail(Photo(1600, 1200));

            Assert.That(thumbnail, Is.Not.Null);
            Assert.That(thumbnail, Does.StartWith("data:image/jpeg;base64,"));

            var bytes = AttachmentStorage.DecodePayload(thumbnail);
            Assert.That(bytes, Is.Not.Null);
            Assert.That(bytes!.Length, Is.LessThan(200 * 1024), "a 1600x1200 photo must compress to a small preview");

            using var decoded = SKBitmap.Decode(bytes);
            Assert.That(decoded, Is.Not.Null);
            Assert.That(Math.Max(decoded!.Width, decoded.Height), Is.LessThanOrEqualTo(320));
            Assert.That(decoded.Width / (float)decoded.Height, Is.EqualTo(1600 / 1200f).Within(0.01f),
                "the aspect ratio has to survive the resize");
        }

        [Test]
        public void AFileThatIsNotAnImageGetsNoThumbnail()
        {
            Assert.That(AttachmentStorage.CreateThumbnail("this is a manual, not a photo"u8.ToArray()), Is.Null);
        }

        [Test]
        public void PayloadDecodingAcceptsDataUrlsAndBareBase64()
        {
            var raw = new byte[] { 1, 2, 3, 4 };

            Assert.That(AttachmentStorage.DecodePayload("data:image/png;base64," + Convert.ToBase64String(raw)),
                Is.EqualTo(raw));
            Assert.That(AttachmentStorage.DecodePayload(Convert.ToBase64String(raw)), Is.EqualTo(raw));
            Assert.That(AttachmentStorage.DecodePayload("not base64 %%%"), Is.Null);
            Assert.That(AttachmentStorage.DecodePayload(null), Is.Null);
        }

        [Test]
        public async Task SavingWritesTheOriginalAndDeleteRemovesIt()
        {
            using var environment = new TempEnvironment();
            var storage = new AttachmentStorage(environment, new ConfigurationBuilder().Build());

            var stored = await storage.SaveAsync(7, "Snare top.png", Photo(64, 48));

            Assert.That(stored.RelativePath, Does.StartWith("media/items/7/"));
            Assert.That(stored.RelativePath, Does.EndWith(".png"));
            var absolute = storage.ToAbsolute(stored.RelativePath);
            Assert.That(File.Exists(absolute), Is.True);
            Assert.That(new FileInfo(absolute).Length, Is.GreaterThan(0));

            storage.Delete(stored.RelativePath);
            Assert.That(File.Exists(absolute), Is.False);

            // Deleting a path that is already gone must not throw.
            storage.Delete(stored.RelativePath);
            storage.Delete(null);
        }

        [Test]
        public async Task UploadIsMovedToDiskAndLeavesOnlyThePreviewInTheRow()
        {
            using var environment = new TempEnvironment();
            var storage = new AttachmentStorage(environment, new ConfigurationBuilder().Build());
            var attachment = new IMCAPI.DB.Entities.Attachment
            {
                ItemId = 3,
                Type = "Photo",
                FileName = "kick.png",
                UploadBase64 = "data:image/png;base64," + Convert.ToBase64String(Photo(800, 800)),
            };

            var error = await storage.ApplyUploadAsync(attachment);

            Assert.That(error, Is.Null);
            Assert.That(attachment.FilePath, Does.StartWith("media/items/3/"));
            Assert.That(attachment.ThumbnailBase64, Does.StartWith("data:image/jpeg;base64,"));
            Assert.That(attachment.UploadBase64, Is.Null, "the original must not stay in the row");
            Assert.That(File.Exists(storage.ToAbsolute(attachment.FilePath!)), Is.True);
        }

        [Test]
        public async Task AnInvalidPayloadIsRejectedWithoutWritingAnything()
        {
            using var environment = new TempEnvironment();
            var storage = new AttachmentStorage(environment, new ConfigurationBuilder().Build());
            var attachment = new IMCAPI.DB.Entities.Attachment { ItemId = 1, UploadBase64 = "%%%not-base64%%%" };

            var error = await storage.ApplyUploadAsync(attachment);

            Assert.That(error, Is.Not.Null);
            Assert.That(attachment.FilePath, Is.Null);
            Assert.That(Directory.Exists(Path.Combine(environment.ContentRootPath, "media")), Is.False);
        }
    }
}
