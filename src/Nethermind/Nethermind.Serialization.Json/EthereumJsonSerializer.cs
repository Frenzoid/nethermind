// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Nethermind.Core.Collections;

namespace Nethermind.Serialization.Json
{
    public class EthereumJsonSerializer : IJsonSerializer
    {
        private JsonSerializerOptions _jsonOptions;

        public EthereumJsonSerializer(int? maxDepth = null)
        {
            if (maxDepth.HasValue)
            {
                _jsonOptions = CreateOptions(indented: false, maxDepth.Value);
            }
            else
            {
                _jsonOptions = JsonOptions;
            }
        }

        public T Deserialize<T>(Stream stream)
        {
            return JsonSerializer.Deserialize<T>(stream, _jsonOptions);
        }

        public T Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }

        public T Deserialize<T>(ref Utf8JsonReader json)
        {
            return JsonSerializer.Deserialize<T>(ref json, _jsonOptions);
        }

        public string Serialize<T>(T value, bool indented = false)
        {
            return JsonSerializer.Serialize<T>(value, indented ? JsonOptionsIndented : _jsonOptions);
        }

        private static JsonSerializerOptions CreateOptions(bool indented, int maxDepth = 64)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = indented,
                IncludeFields = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                MaxDepth = maxDepth,
                Converters =
                {
                    new LongConverter(),
                    new UInt256Converter(),
                    new ULongConverter(),
                    new IntConverter(),
                    new ByteArrayConverter(),
                    new NullableLongConverter(),
                    new NullableULongConverter(),
                    new NullableUInt256Converter(),
                    new NullableIntConverter(),
                    new TxTypeConverter(),
                    new DoubleConverter(),
                    new DoubleArrayConverter(),
                    new BooleanConverter(),
                    new DictionaryAddressKeyConverter(),
                    new MemoryByteConverter(),
                }
            };

            foreach (var converter in _additionalConverters)
            {
                options.Converters.Add(converter);
            }

            return options;
        }

        private static List<JsonConverter> _additionalConverters = new();
        public static void AddConverter(JsonConverter converter)
        {
            _additionalConverters.Add(converter);

            JsonOptions = CreateOptions(indented: false);
            JsonOptionsIndented = CreateOptions(indented: true);
        }

        public static JsonSerializerOptions JsonOptions { get; private set; } = CreateOptions(indented: false);

        public static JsonSerializerOptions JsonOptionsIndented { get; private set; } = CreateOptions(indented: true);

        private static CountingStream GetStream(Stream stream)
        {
            return new CountingStream(stream);
        }

        public long Serialize<T>(Stream stream, T value, bool indented = false)
        {
            CountingStream countingStream = GetStream(stream);
            JsonSerializer.Serialize(countingStream, value, indented ? JsonOptionsIndented : _jsonOptions);
            long position = countingStream.Position;
            return position;
        }

        public async ValueTask<long> SerializeAsync<T>(Stream stream, T value, bool indented = false)
        {
            CountingStream countingStream = GetStream(stream);
            await JsonSerializer.SerializeAsync(countingStream, value, indented ? JsonOptionsIndented : _jsonOptions);
            long position = countingStream.Position;
            return position;
        }

        public static void SerializeToStream<T>(Stream stream, T value, bool indented = false)
        {
            JsonSerializer.Serialize(stream, value, indented ? JsonOptionsIndented : JsonOptions);
        }

        private sealed class CountingStream : Stream
        {
            private Stream _wrappedStream;
            private long _position;

            public CountingStream(Stream stream)
            {
                _wrappedStream = stream;
            }

            public override long Position { get => _position; set => throw new NotSupportedException(); }

            public override void Flush() => _wrappedStream.Flush();

            public override void Write(byte[] buffer, int offset, int count)
            {
                _position += count;
                _wrappedStream.Write(buffer, offset, count);
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                _position += buffer.Length;
                _wrappedStream.Write(buffer);
            }
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
            {
                _position += count;
                return _wrappedStream.WriteAsync(buffer, offset, count, token);
            }

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token)
            {
                _position += buffer.Length;
                return _wrappedStream.WriteAsync(buffer, token);
            }

            public override bool CanRead => _wrappedStream.CanRead;
            public override bool CanSeek => _wrappedStream.CanSeek;
            public override bool CanWrite => _wrappedStream.CanWrite;
            public override long Length => _wrappedStream.Length;
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }

    public static class JsonElementExtensions
    {
        public static bool TryGetSubProperty(this JsonElement element, string innerPath, out JsonElement value)
        {
            ArgumentNullException.ThrowIfNullOrEmpty(innerPath);

            if (innerPath.Contains('.'))
            {
                string[] parts = innerPath.Split('.');
                JsonElement currentElement = element;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (!currentElement.TryGetProperty(parts[i], out currentElement))
                    {
                        value = default;
                        return false;
                    }
                }
                return currentElement.TryGetProperty(parts[^1], out value);
            }

            return element.TryGetProperty(innerPath.AsSpan(), out value);
        }
    }
}
