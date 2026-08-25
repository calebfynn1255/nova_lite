using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntimeGenAI;
using System.Collections.Generic;

namespace NovaLite.UI.Services;

public static class OnnxVisionCaptioner
{
    private static readonly string ModelDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
        "NovaLite", "models", "vision", "phi3-vision-128k-instruct-onnx-cpu");

    private static Model? _model;
    private static MultiModalProcessor? _processor;

    public static async Task<string> GenerateCaptionAsync(string imagePath)
    {
        try
        {
            if (!Directory.Exists(ModelDir) || !File.Exists(Path.Combine(ModelDir, "genai_config.json")))
            {
                // Note: The Phi-3-Vision ONNX model is large (~2-3GB).
                // If it's missing, just return null so we don't confuse the text LLM with error messages.
                return null;
            }

            if (_model == null)
            {
                _model = new Model(ModelDir);
                _processor = new MultiModalProcessor(_model);
            }

            // Phi-3 Vision prompt template
            var prompt = "<|user|>\n<|image_1|>\nDescribe the image in detail.<|end|>\n<|assistant|>\n";

            using var images = Images.Load([imagePath]);
            using var inputTensors = _processor!.ProcessImages(prompt, images);

            using var generatorParams = new GeneratorParams(_model);
            generatorParams.SetSearchOption("max_length", 300);
            generatorParams.SetInputs(inputTensors);

            using var generator = new Generator(_model, generatorParams);
            using var tokenizerStream = _processor.CreateStream();
            
            var sb = new StringBuilder();
            
            // Run inference sync here for now, it's fast enough on small max_length, 
            // but ideally could be offloaded to Task.Run
            await Task.Run(() => 
            {
                while (!generator.IsDone())
                {
                    generator.ComputeLogits();
                    generator.GenerateNextToken();
                    var token = generator.GetSequence(0)[^1];
                    var tokenStr = tokenizerStream.Decode(token);
                    sb.Append(tokenStr);
                }
            });

            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            return $"[Captioning error: {ex.Message}]";
        }
    }
}
