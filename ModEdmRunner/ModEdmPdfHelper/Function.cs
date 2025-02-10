using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Amazon.Lambda.Core;
using GroupDocs.Parser;
using GroupDocs.Parser.Data;
using GroupDocs.Parser.Options;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ModEdmPdfHelper
{
    public class PdfRequest
    {
        public string Action { get; set; }
        public string Base64String { get; set; }
    }

    public class PdfResponse
    {
        public List<string> ImagesBase64String { get; set; }
    }

    public class Function
    {
        public PdfResponse FunctionHandler(PdfRequest input, ILambdaContext context)
        {
            if (input.Action != "extractTextFromBuffer")
            {
                throw new ArgumentException("Invalid action provided.");
            }

            byte[] pdfBytes;
            try
            {
                pdfBytes = Convert.FromBase64String(input.Base64String);
            }
            catch (FormatException ex)
            {
                context.Logger.LogError($"Invalid Base64 string: {ex.Message}");
                throw new ArgumentException("Invalid Base64 string provided.");
            }

            List<string> imagesBase64 = new List<string>();

            using (Stream pdfStream = new MemoryStream(pdfBytes))
            using (Parser parser = new Parser(pdfStream))
            {
                IEnumerable<PageImageArea> images = parser.GetImages();

                if (images == null || !images.Any())
                {
                    context.Logger.LogError("No images found in PDF.");
                    return new PdfResponse { ImagesBase64String = new List<string>() };
                }

                ImageOptions options = new ImageOptions(ImageFormat.Png); // Try PNG instead of JPEG

                foreach (PageImageArea image in images)
                {
                    try
                    {
                        using (Stream imageStream = image.GetImageStream(options))
                        {
                            byte[] imageBytes = StreamToByteArray(imageStream);
                            string base64Image = Convert.ToBase64String(imageBytes);
                            imagesBase64.Add(base64Image);
                        }
                    }
                    catch (Exception ex)
                    {
                        context.Logger.LogError($"Error extracting image: {ex.Message}");
                    }
                }
            }

            return new PdfResponse { ImagesBase64String = imagesBase64 };
        }

        private static byte[] StreamToByteArray(Stream input)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                input.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
        }
    }
}
