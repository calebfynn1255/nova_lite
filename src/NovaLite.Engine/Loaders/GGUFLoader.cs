using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using NovaLite.Core.Interfaces;
using NovaLite.Core.Models;
using NovaLite.Native;

namespace NovaLite.Engine.Loaders;

public sealed class GGUFLoader : IModelLoader
{
    private readonly ILogger<GGUFLoader> _logger;

    public IReadOnlyList<string> SupportedExtensions { get; } = [".gguf"];

    public GGUFLoader(ILogger<GGUFLoader> logger)
    {
        _logger = logger;
    }

    public bool CanLoad(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        Span<byte> magic = stackalloc byte[4];
        using var fs = File.OpenRead(filePath);
        return fs.Read(magic) == 4 &&
               magic[0] == 0x47 && magic[1] == 0x47 &&
               magic[2] == 0x55 && magic[3] == 0x46;
    }

    public unsafe Task<LoadedModel> LoadAsync(string filePath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            _logger.LogInformation("Loading GGUF model via llama.cpp 2.24.0: {Path}", filePath);

            NativeLoader.EnsureLoaded();
            LlamaCppBindings.llama_backend_init();

            // Model params
            var modelParams = LlamaCppBindings.llama_model_default_params();
            
            var settings = NovaLite.Core.Settings.AppSettings.Load();
            var configuredGpuLayers = Math.Max(0, settings.GpuLayers);
            modelParams.n_gpu_layers = configuredGpuLayers;
            modelParams.main_gpu = 0;
            modelParams.use_extra_bufts = 0;
            modelParams.use_mmap = 1;
            modelParams.use_mlock = 0;
            modelParams.no_alloc = 0;
            modelParams.split_mode = 0;
            
            _logger.LogInformation("LlamaModelParams: gpuLayers={GpuLayers}, mainGpu={MainGpu}, devices={Devices}, mmap={Mmap}, useMlock={UseMlock}",
                modelParams.n_gpu_layers, modelParams.main_gpu, modelParams.devices, modelParams.use_mmap, modelParams.use_mlock);

            // Load model — 2.24.0 API: llama_model_load_from_file takes byte* path
            IntPtr model;
            byte[] pathBytes = Encoding.UTF8.GetBytes(filePath + "\0");
            fixed (byte* pPath = pathBytes)
            {
                model = LlamaCppBindings.llama_model_load_from_file(pPath, modelParams);
            }

            if (model == IntPtr.Zero)
                throw new Exception($"llama.cpp failed to load: {filePath}");

            _logger.LogInformation("Model loaded, getting vocab...");

            // Get vocab pointer (new in 2.24.0 - tokenize uses vocab* not model*)
            IntPtr vocab = LlamaCppBindings.llama_model_get_vocab(model);
            if (vocab == IntPtr.Zero)
                throw new Exception("llama_model_get_vocab returned null");

            // Context params
            var ctxParams = LlamaCppBindings.llama_context_default_params();
            ctxParams.n_ctx = 4096;
            ctxParams.n_batch = 512;
            ctxParams.n_ubatch = 512;
            ctxParams.n_threads = Math.Min(Environment.ProcessorCount, 4);
            ctxParams.n_threads_batch = Math.Min(Environment.ProcessorCount, 4);

            // Create context — 2.24.0 API: llama_init_from_model
            IntPtr ctx = LlamaCppBindings.llama_init_from_model(model, ctxParams);
            if (ctx == IntPtr.Zero)
                throw new Exception("llama_init_from_model returned null");

            _logger.LogInformation("Context created, building sampler chain...");

            // Build sampler chain — new 2.24.0 API
            var chainParams = LlamaCppBindings.llama_sampler_chain_default_params();
            chainParams.no_perf = 1;
            IntPtr sampler = LlamaCppBindings.llama_sampler_chain_init(chainParams);
            LlamaCppBindings.llama_sampler_chain_add(sampler, LlamaCppBindings.llama_sampler_init_top_k(40));
            LlamaCppBindings.llama_sampler_chain_add(sampler, LlamaCppBindings.llama_sampler_init_top_p(0.95f, (UIntPtr)1));
            LlamaCppBindings.llama_sampler_chain_add(sampler, LlamaCppBindings.llama_sampler_init_temp(0.7f));
            LlamaCppBindings.llama_sampler_chain_add(sampler, LlamaCppBindings.llama_sampler_init_dist(0xDEADBEEF));

            _logger.LogInformation("Sampler chain built.");

            var session = new LlamaSession(model, vocab, ctx, sampler, _logger);
            var fi = new FileInfo(filePath);

            var loadedModel = new LoadedModel
            {
                FilePath = filePath,
                Format = "GGUF",
                DisplayName = Path.GetFileNameWithoutExtension(filePath),
                FileSizeBytes = fi.Length,
                NativeHandle = model,
                State = session
            };

            loadedModel.RegisterDispose(() =>
            {
                _logger.LogDebug("Freeing model: {Name}", loadedModel.DisplayName);
                session.Dispose();
                LlamaCppBindings.llama_model_free(model);
                LlamaCppBindings.llama_backend_free();
            });

            _logger.LogInformation("GGUF model loaded successfully: {Name}", loadedModel.DisplayName);
            return loadedModel;

        }, ct);
    }

    public void Unload(LoadedModel model) => model.Dispose();
}
