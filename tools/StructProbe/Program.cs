using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

[assembly: DisableRuntimeMarshalling]

[StructLayout(LayoutKind.Sequential)]
public struct LlamaModelParams
{
    public int n_gpu_layers;
    public int split_mode;
    public int main_gpu;
    public IntPtr tensor_split;
    public IntPtr rpc_servers;
    public IntPtr progress_callback;
    public IntPtr progress_callback_user_data;
    public IntPtr kv_overrides;
    public IntPtr devices;
    public bool vocab_only;
    public bool use_mmap;
    public bool use_mlock;
    public bool check_tensors;
}

[StructLayout(LayoutKind.Sequential)]
public struct LlamaContextParams
{
    public IntPtr model;
    public IntPtr devices;
    public uint n_ctx;
    public uint n_batch;
    public uint n_ubatch;
    public uint n_seq_max;
    public uint n_threads;
    public uint n_threads_batch;
    public int rope_scaling_type;
    public int pooling_type;
    public int attention_type;
    public float rope_freq_base;
    public float rope_freq_scale;
    public float yarn_ext_factor;
    public float yarn_attn_factor;
    public float yarn_beta_fast;
    public float yarn_beta_slow;
    public uint yarn_orig_ctx;
    public float defrag_thold;
    public IntPtr cb_eval;
    public IntPtr cb_eval_user_data;
    public int type_k;
    public int type_v;
    public bool swa_full;
    public bool logits_all;
    public bool embeddings;
    public bool offload_kqv;
    public bool flash_attn;
    public bool no_perf;
    public IntPtr abort_callback;
    public IntPtr abort_callback_data;
}

static unsafe partial class Native
{
    [LibraryImport("llama", EntryPoint = "llama_backend_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void llama_backend_init();

    [LibraryImport("llama", EntryPoint = "llama_model_default_params")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial LlamaModelParams llama_model_default_params();

    [LibraryImport("llama", EntryPoint = "llama_context_default_params")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial LlamaContextParams llama_context_default_params();
}

static class Program
{
    static unsafe void Main()
    {
        ExportChecker.Run();
        var publishDir = @"C:\Users\Fynn\nova_lite\src\NovaLite.UI\bin\Release\net9.0\win-x64\publish";
        NativeLibrary.Load(Path.Combine(publishDir, "llama.dll"));

        Console.WriteLine($"sizeof(LlamaModelParams)   = {Marshal.SizeOf<LlamaModelParams>()}");
        Console.WriteLine($"sizeof(LlamaContextParams) = {Marshal.SizeOf<LlamaContextParams>()}");

        Native.llama_backend_init();

        var mp = Native.llama_model_default_params();
        Console.WriteLine($"\nllama_model_default_params():");
        Console.WriteLine($"  n_gpu_layers  = {mp.n_gpu_layers}");
        Console.WriteLine($"  split_mode    = {mp.split_mode}");
        Console.WriteLine($"  main_gpu      = {mp.main_gpu}");
        Console.WriteLine($"  vocab_only    = {mp.vocab_only}");
        Console.WriteLine($"  use_mmap      = {mp.use_mmap}");
        Console.WriteLine($"  use_mlock     = {mp.use_mlock}");
        Console.WriteLine($"  check_tensors = {mp.check_tensors}");
        {
            var raw = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref mp, 1));
            Console.Write($"  raw[{raw.Length}]: ");
            foreach (var b in raw) Console.Write($"{b:X2} ");
            Console.WriteLine();
        }

        var cp = Native.llama_context_default_params();
        Console.WriteLine($"\nllama_context_default_params():");
        Console.WriteLine($"  n_ctx           = {cp.n_ctx}");
        Console.WriteLine($"  n_batch         = {cp.n_batch}");
        Console.WriteLine($"  n_ubatch        = {cp.n_ubatch}");
        Console.WriteLine($"  n_threads       = {cp.n_threads}");
        Console.WriteLine($"  n_threads_batch = {cp.n_threads_batch}");
        Console.WriteLine($"  flash_attn      = {cp.flash_attn}");
        Console.WriteLine($"  offload_kqv     = {cp.offload_kqv}");
        {
            var raw = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref cp, 1));
            Console.Write($"  raw[{raw.Length}]: ");
            foreach (var b in raw) Console.Write($"{b:X2} ");
            Console.WriteLine();
        }
    }
}
