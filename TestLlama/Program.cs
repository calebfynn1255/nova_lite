using System.Text;
using NovaLite.Native;

internal static class Program
{
    private static unsafe int Main(string[] args)
    {
        string path = args.FirstOrDefault() ?? @"C:\Users\Fynn\NovaLiteModels\Llama_3.2_1B.gguf";
        byte[] pathBytes = Encoding.UTF8.GetBytes(path + '\0');

        NativeLoader.EnsureLoaded();
        LlamaCppBindings.llama_backend_init();
        try
        {
            var parameters = LlamaCppBindings.llama_model_default_params();
            parameters.n_gpu_layers = 0;
            parameters.use_extra_bufts = 0;

            Console.WriteLine($"Loading model: {path}");
            IntPtr model;
            fixed (byte* pPath = pathBytes)
                model = LlamaCppBindings.llama_model_load_from_file(pPath, parameters);

            if (model == IntPtr.Zero)
            {
                Console.Error.WriteLine("llama.cpp returned a null model handle.");
                return 1;
            }

            var contextParameters = LlamaCppBindings.llama_context_default_params();
            contextParameters.n_ctx = 2048;
            contextParameters.n_batch = 512;
            contextParameters.n_ubatch = 512;
            contextParameters.n_threads = Math.Min(Environment.ProcessorCount, 4);
            contextParameters.n_threads_batch = Math.Min(Environment.ProcessorCount, 4);

            IntPtr context = LlamaCppBindings.llama_init_from_model(model, contextParameters);
            if (context == IntPtr.Zero)
            {
                Console.Error.WriteLine("llama.cpp could not create a context.");
                LlamaCppBindings.llama_model_free(model);
                return 1;
            }

            LlamaCppBindings.llama_free(context);
            LlamaCppBindings.llama_model_free(model);
            Console.WriteLine("Model and inference context loaded successfully.");
            return 0;
        }
        finally
        {
            LlamaCppBindings.llama_backend_free();
        }
    }
}
