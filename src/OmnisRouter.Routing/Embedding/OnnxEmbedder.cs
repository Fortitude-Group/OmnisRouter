using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using OmnisRouter.Core.Abstractions;

namespace OmnisRouter.Routing.Embedding;

/// <summary>Configuration for <see cref="OnnxEmbedder"/> — the pinned model + tokenizer assets.</summary>
public sealed record OnnxEmbedderOptions
{
    /// <summary>Path to the ONNX model (bge-small-en-v1.5, int8).</summary>
    public required string ModelPath { get; init; }

    /// <summary>Path to the WordPiece vocab.txt for the BERT tokenizer.</summary>
    public required string VocabPath { get; init; }

    public int Dimension { get; init; } = 384;

    public int MaxSequenceLength { get; init; } = 512;
}

/// <summary>
/// In-process sentence embedder over ONNX Runtime with the pinned <c>bge-small-en-v1.5</c> model
/// (CLS pooling + L2 normalize; no network hop). Tokenization uses the in-box BERT WordPiece
/// tokenizer. See research.md R1.
/// </summary>
public sealed class OnnxEmbedder : IEmbedder, IDisposable
{
    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly OnnxEmbedderOptions _options;

    public OnnxEmbedder(OnnxEmbedderOptions options)
    {
        if (!File.Exists(options.ModelPath))
        {
            throw new FileNotFoundException($"ONNX embedder model not found at '{options.ModelPath}'.", options.ModelPath);
        }

        if (!File.Exists(options.VocabPath))
        {
            throw new FileNotFoundException($"Tokenizer vocab not found at '{options.VocabPath}'.", options.VocabPath);
        }

        _options = options;
        _session = new InferenceSession(options.ModelPath);
        using var vocabStream = File.OpenRead(options.VocabPath);
        _tokenizer = BertTokenizer.Create(vocabStream);
    }

    public int Dimension => _options.Dimension;

    public float[] Embed(string text)
    {
        var ids = _tokenizer.EncodeToIds(text ?? string.Empty);
        var length = Math.Min(ids.Count, _options.MaxSequenceLength);

        var inputIds = new DenseTensor<long>([1, length]);
        var attentionMask = new DenseTensor<long>([1, length]);
        var tokenTypeIds = new DenseTensor<long>([1, length]);
        for (var i = 0; i < length; i++)
        {
            inputIds[0, i] = ids[i];
            attentionMask[0, i] = 1;
            tokenTypeIds[0, i] = 0;
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds),
        };

        using var results = _session.Run(inputs);
        var hidden = results.First().AsTensor<float>(); // [1, seq, dim]

        // CLS pooling: take the first token's vector, then L2-normalize.
        var vector = new float[_options.Dimension];
        for (var d = 0; d < _options.Dimension; d++)
        {
            vector[d] = hidden[0, 0, d];
        }

        Normalize(vector);
        return vector;
    }

    private static void Normalize(float[] v)
    {
        double sumSq = 0;
        foreach (var x in v)
        {
            sumSq += x * (double)x;
        }

        var norm = Math.Sqrt(sumSq);
        if (norm <= 0)
        {
            return;
        }

        for (var i = 0; i < v.Length; i++)
        {
            v[i] = (float)(v[i] / norm);
        }
    }

    public void Dispose() => _session.Dispose();
}
