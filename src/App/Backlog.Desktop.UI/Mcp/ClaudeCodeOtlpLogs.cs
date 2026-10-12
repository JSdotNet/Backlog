using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// Reads an OTLP logs export — <c>ExportLogsServiceRequest</c>, in either of the two
/// encodings OTLP/HTTP allows — and keeps Claude Code's <c>claude_code.api_request</c>
/// events, as <see cref="ClaudeApiRequest"/>s. Every other event is dropped here.
/// <para>
/// <b>Written out rather than taken from a package.</b> The OpenTelemetry .NET SDK
/// exports and does not receive: its generated protobuf types are internal, and the
/// only public ones are behind a collector or a gRPC stack this application does not
/// otherwise need. What a receiver of one event needs from the protocol is small and
/// fixed — five nested messages, four scalar kinds — so it is decoded by hand, and
/// every field this reader does not use is skipped by its wire type, which is what
/// keeps a newer exporter's fields from breaking it.
/// </para>
/// <para>
/// <b>Which event.</b> Claude Code names the event three ways at once: the record's
/// body is <c>claude_code.api_request</c>, its <c>event.name</c> attribute is
/// <c>api_request</c>, and a newer exporter may set the record's own
/// <c>event_name</c>. Any one of them is enough.
/// </para>
/// <para>
/// <b>Which events are kept.</b> An api_request with no <c>request_id</c> is dropped
/// too: storing is idempotent by that id, and an event without one could not be told
/// from its own retry.
/// </para>
/// </summary>
public static class ClaudeCodeOtlpLogs
{
    /// <summary>The event this reader keeps, as the record's body and its
    /// <c>event_name</c> spell it.</summary>
    public const string ApiRequestEvent = "claude_code.api_request";

    /// <summary>The same event as the <c>event.name</c> attribute spells it.</summary>
    private const string ApiRequestAttribute = "api_request";

    /// <summary>Reads an OTLP/JSON body. Throws <see cref="FormatException"/> for one
    /// that is not an export request.</summary>
    public static IReadOnlyList<ClaudeApiRequest> ReadJson(ReadOnlyMemory<byte> body)
    {
        JsonDocument document;

        try
        {
            // A UTF-8 byte order mark is skipped, which the reader does not do by itself.
            // Parse rather than ParseValue: it refuses anything after the one value,
            // where ParseValue would stop reading there and call it a success.
            if (body.Span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) body = body[3..];

            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The body is not JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object) throw new FormatException("The body is not a JSON object.");

            var requests = new List<ClaudeApiRequest>();

            foreach (var resourceLogs in Array(root, "resourceLogs", "resource_logs"))
            {
                foreach (var scopeLogs in Array(resourceLogs, "scopeLogs", "scope_logs"))
                {
                    foreach (var record in Array(scopeLogs, "logRecords", "log_records"))
                    {
                        if (record.ValueKind is not JsonValueKind.Object) continue;

                        var log = new LogRecord
                        {
                            TimeUnixNano = JsonUnsigned(Property(record, "timeUnixNano", "time_unix_nano")),
                            ObservedTimeUnixNano = JsonUnsigned(Property(record, "observedTimeUnixNano", "observed_time_unix_nano")),
                            EventName = Property(record, "eventName", "event_name") is { ValueKind: JsonValueKind.String } name ? name.GetString() : null,
                            Body = Property(record, "body", "body") is { } bodyValue ? JsonScalar(bodyValue) as string : null
                        };

                        foreach (var attribute in Array(record, "attributes", "attributes"))
                        {
                            if (Property(attribute, "key", "key") is not { ValueKind: JsonValueKind.String } key) continue;
                            if (Property(attribute, "value", "value") is not { } value) continue;
                            if (JsonScalar(value) is { } scalar) log.Attributes[key.GetString()!] = scalar;
                        }

                        if (ToApiRequest(log) is { } request) requests.Add(request);
                    }
                }
            }

            return requests;
        }
    }

    /// <summary>Reads an OTLP/protobuf body. Throws <see cref="FormatException"/> for
    /// one that is not a well-formed protobuf message.</summary>
    public static IReadOnlyList<ClaudeApiRequest> ReadProtobuf(ReadOnlySpan<byte> body)
    {
        var requests = new List<ClaudeApiRequest>();
        var reader = new ProtoReader(body);

        // ExportLogsServiceRequest { repeated ResourceLogs resource_logs = 1; }
        while (reader.Next(out var field, out var wire))
        {
            if (field == 1 && wire == ProtoReader.WireLengthDelimited) ReadResourceLogs(reader.Bytes(), requests);
            else reader.Skip(wire);
        }

        return requests;
    }

    // ResourceLogs { Resource resource = 1; repeated ScopeLogs scope_logs = 2; string schema_url = 3; }
    private static void ReadResourceLogs(ReadOnlySpan<byte> message, List<ClaudeApiRequest> requests)
    {
        var reader = new ProtoReader(message);

        while (reader.Next(out var field, out var wire))
        {
            if (field == 2 && wire == ProtoReader.WireLengthDelimited) ReadScopeLogs(reader.Bytes(), requests);
            else reader.Skip(wire);
        }
    }

    // ScopeLogs { InstrumentationScope scope = 1; repeated LogRecord log_records = 2; string schema_url = 3; }
    private static void ReadScopeLogs(ReadOnlySpan<byte> message, List<ClaudeApiRequest> requests)
    {
        var reader = new ProtoReader(message);

        while (reader.Next(out var field, out var wire))
        {
            if (field == 2 && wire == ProtoReader.WireLengthDelimited)
            {
                if (ToApiRequest(ReadLogRecord(reader.Bytes())) is { } request) requests.Add(request);
            }
            else
            {
                reader.Skip(wire);
            }
        }
    }

    // LogRecord { fixed64 time_unix_nano = 1; ... AnyValue body = 5; repeated KeyValue attributes = 6;
    //             ... fixed64 observed_time_unix_nano = 11; string event_name = 12; }
    private static LogRecord ReadLogRecord(ReadOnlySpan<byte> message)
    {
        var log = new LogRecord();
        var reader = new ProtoReader(message);

        while (reader.Next(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == ProtoReader.WireFixed64:
                    log.TimeUnixNano = reader.Fixed64();
                    break;
                case 11 when wire == ProtoReader.WireFixed64:
                    log.ObservedTimeUnixNano = reader.Fixed64();
                    break;
                case 5 when wire == ProtoReader.WireLengthDelimited:
                    log.Body = ReadAnyValue(reader.Bytes()) as string;
                    break;
                case 6 when wire == ProtoReader.WireLengthDelimited:
                    ReadKeyValue(reader.Bytes(), log.Attributes);
                    break;
                case 12 when wire == ProtoReader.WireLengthDelimited:
                    log.EventName = Encoding.UTF8.GetString(reader.Bytes());
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        return log;
    }

    // KeyValue { string key = 1; AnyValue value = 2; }
    private static void ReadKeyValue(ReadOnlySpan<byte> message, Dictionary<string, object> attributes)
    {
        string? key = null;
        object? value = null;
        var reader = new ProtoReader(message);

        while (reader.Next(out var field, out var wire))
        {
            if (field == 1 && wire == ProtoReader.WireLengthDelimited) key = Encoding.UTF8.GetString(reader.Bytes());
            else if (field == 2 && wire == ProtoReader.WireLengthDelimited) value = ReadAnyValue(reader.Bytes());
            else reader.Skip(wire);
        }

        if (key is not null && value is not null) attributes[key] = value;
    }

    // AnyValue { oneof value { string string_value = 1; bool bool_value = 2; int64 int_value = 3;
    //            double double_value = 4; ArrayValue array_value = 5; KeyValueList kvlist_value = 6; bytes bytes_value = 7; } }
    // Arrays, maps and bytes are read past: nothing this reader keeps is one.
    private static object? ReadAnyValue(ReadOnlySpan<byte> message)
    {
        object? value = null;
        var reader = new ProtoReader(message);

        while (reader.Next(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == ProtoReader.WireLengthDelimited:
                    value = Encoding.UTF8.GetString(reader.Bytes());
                    break;
                case 2 when wire == ProtoReader.WireVarint:
                    value = reader.Varint() != 0;
                    break;
                case 3 when wire == ProtoReader.WireVarint:
                    value = unchecked((long)reader.Varint());
                    break;
                case 4 when wire == ProtoReader.WireFixed64:
                    value = BitConverter.UInt64BitsToDouble(reader.Fixed64());
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        return value;
    }

    /// <summary>The one record, as a request — or null for every event this reader
    /// does not keep.</summary>
    private static ClaudeApiRequest? ToApiRequest(LogRecord log)
    {
        var isApiRequest =
            string.Equals(log.Body, ApiRequestEvent, StringComparison.Ordinal)
            || string.Equals(log.EventName, ApiRequestEvent, StringComparison.Ordinal)
            || string.Equals(log.Text("event.name"), ApiRequestAttribute, StringComparison.Ordinal);

        if (!isApiRequest) return null;

        if (log.Text("request_id") is not { Length: > 0 } requestId) return null;

        return new ClaudeApiRequest(
            requestId,
            log.Text("session.id"),
            Timestamp(log),
            log.Text("model"),
            log.Text("effort"),
            log.Integer("cost_usd_micros") ?? Micros(log.Number("cost_usd")),
            log.Integer("input_tokens"),
            log.Integer("output_tokens"),
            log.Integer("cache_read_tokens"),
            log.Integer("cache_creation_tokens"),
            log.Integer("duration_ms"),
            log.Text("query_source"),
            log.Text("agent.name"),
            log.Text("skill.name"),
            log.Text("prompt.id"));
    }

    /// <summary><c>event.timestamp</c> first, because it is the attribute the
    /// specification names; then the record's own time, then the time it was
    /// observed. A record with none of the three is dated the epoch rather than
    /// "now": a receiver's clock is not when the request was made, and an obviously
    /// wrong date is easier to recognise than a plausible one.</summary>
    private static DateTimeOffset Timestamp(LogRecord log)
    {
        if (log.Text("event.timestamp") is { } text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        var nanos = log.TimeUnixNano is > 0 ? log.TimeUnixNano : log.ObservedTimeUnixNano;

        return nanos is > 0
            ? DateTimeOffset.UnixEpoch.AddTicks((long)(nanos.Value / 100))
            : DateTimeOffset.UnixEpoch;
    }

    /// <summary>Dollars to whole micro-dollars. A figure too large for a 64-bit count is
    /// not a request's cost, and is left unknown rather than clamped.</summary>
    private static long? Micros(double? dollars) =>
        dollars is { } value && double.IsFinite(value) && Math.Abs(value) < 9e12
            ? (long)Math.Round(value * 1_000_000d, MidpointRounding.AwayFromZero)
            : null;

    // --- JSON helpers -----------------------------------------------------------

    /// <summary>OTLP/JSON spells field names in lowerCamelCase; a receiver is asked to
    /// accept the protobuf field names too, so both are looked for.</summary>
    private static JsonElement? Property(JsonElement element, string camel, string snake)
    {
        if (element.ValueKind is not JsonValueKind.Object) return null;
        if (element.TryGetProperty(camel, out var value)) return value;
        if (!ReferenceEquals(camel, snake) && element.TryGetProperty(snake, out value)) return value;
        return null;
    }

    private static IEnumerable<JsonElement> Array(JsonElement element, string camel, string snake) =>
        Property(element, camel, snake) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];

    /// <summary>An <c>AnyValue</c>'s scalar. In OTLP/JSON a 64-bit integer may be a
    /// number or a decimal string, and both are taken.</summary>
    private static object? JsonScalar(JsonElement anyValue)
    {
        if (anyValue.ValueKind is not JsonValueKind.Object) return null;

        if (Property(anyValue, "stringValue", "string_value") is { ValueKind: JsonValueKind.String } text) return text.GetString();
        if (Property(anyValue, "boolValue", "bool_value") is { ValueKind: JsonValueKind.True or JsonValueKind.False } flag) return flag.GetBoolean();

        if (Property(anyValue, "intValue", "int_value") is { } integer)
        {
            if (integer.ValueKind is JsonValueKind.Number && integer.TryGetInt64(out var number)) return number;
            if (integer.ValueKind is JsonValueKind.String
                && long.TryParse(integer.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            return null;
        }

        if (Property(anyValue, "doubleValue", "double_value") is { } real)
        {
            if (real.ValueKind is JsonValueKind.Number && real.TryGetDouble(out var number)) return number;
            if (real.ValueKind is JsonValueKind.String
                && double.TryParse(real.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }

        return null;
    }

    /// <summary>A <c>fixed64</c> in OTLP/JSON: a decimal string, or a number from an
    /// exporter that did not quote it.</summary>
    private static ulong? JsonUnsigned(JsonElement? element) => element switch
    {
        { ValueKind: JsonValueKind.String } text when ulong.TryParse(text.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) => parsed,
        { ValueKind: JsonValueKind.Number } number when number.TryGetUInt64(out var parsed) => parsed,
        _ => null
    };

    // --- The record, whichever encoding it came in ------------------------------

    private sealed class LogRecord
    {
        public ulong? TimeUnixNano { get; set; }

        public ulong? ObservedTimeUnixNano { get; set; }

        public string? Body { get; set; }

        public string? EventName { get; set; }

        public Dictionary<string, object> Attributes { get; } = new(StringComparer.Ordinal);

        public string? Text(string key) => Attributes.TryGetValue(key, out var value) && value is string text && text.Length > 0 ? text : null;

        /// <summary>A whole number, however the exporter typed it: Claude Code sends
        /// counts as <c>intValue</c>, but a string or a whole <c>doubleValue</c> means
        /// the same count.</summary>
        public long? Integer(string key) => Attributes.TryGetValue(key, out var value)
            ? value switch
            {
                long number => number,
                double real when double.IsFinite(real) && real == Math.Floor(real) && Math.Abs(real) < 9e18 => (long)real,
                string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => null
            }
            : null;

        public double? Number(string key) => Attributes.TryGetValue(key, out var value)
            ? value switch
            {
                double real => real,
                long number => number,
                string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => null
            }
            : null;
    }

    // --- Protobuf wire format ---------------------------------------------------

    /// <summary>
    /// The protobuf wire format, read forward over one message. Only what the four
    /// wire types need: varints, fixed 64- and 32-bit values, and length-delimited
    /// slices. Groups (wire types 3 and 4) were deprecated before OTLP existed, and a
    /// message carrying one is refused rather than guessed at.
    /// </summary>
    private ref struct ProtoReader(ReadOnlySpan<byte> buffer)
    {
        public const int WireVarint = 0;
        public const int WireFixed64 = 1;
        public const int WireLengthDelimited = 2;
        public const int WireFixed32 = 5;

        private const ulong MaxFieldNumber = (1UL << 29) - 1;

        private readonly ReadOnlySpan<byte> _buffer = buffer;
        private int _position;

        public bool Next(out int field, out int wire)
        {
            if (_position >= _buffer.Length)
            {
                field = 0;
                wire = 0;
                return false;
            }

            var key = Varint();

            // Protobuf field numbers run from 1 to 2^29 - 1; a tag outside that is not a
            // message, and is refused before the cast could wrap it into a small one.
            if (key >> 3 is 0 or > MaxFieldNumber) throw new FormatException("A protobuf field number is out of range.");

            field = (int)(key >> 3);
            wire = (int)(key & 7);

            return true;
        }

        public ulong Varint()
        {
            ulong result = 0;

            for (var shift = 0; shift < 64; shift += 7)
            {
                if (_position >= _buffer.Length) throw new FormatException("A protobuf varint runs past the end of the message.");

                var b = _buffer[_position++];
                result |= (ulong)(b & 0x7F) << shift;

                if ((b & 0x80) == 0) return result;
            }

            throw new FormatException("A protobuf varint is longer than ten bytes.");
        }

        public ulong Fixed64()
        {
            if (_buffer.Length - _position < 8) throw new FormatException("A protobuf fixed64 runs past the end of the message.");

            var value = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.Slice(_position, 8));
            _position += 8;
            return value;
        }

        public ReadOnlySpan<byte> Bytes()
        {
            var length = Varint();

            if (length > (ulong)(_buffer.Length - _position)) throw new FormatException("A protobuf field runs past the end of the message.");

            var slice = _buffer.Slice(_position, (int)length);
            _position += (int)length;
            return slice;
        }

        public void Skip(int wire)
        {
            switch (wire)
            {
                case WireVarint:
                    Varint();
                    break;
                case WireFixed64:
                    Fixed64();
                    break;
                case WireLengthDelimited:
                    Bytes();
                    break;
                case WireFixed32:
                    if (_buffer.Length - _position < 4) throw new FormatException("A protobuf fixed32 runs past the end of the message.");
                    _position += 4;
                    break;
                default:
                    throw new FormatException($"Protobuf wire type {wire} is not one OTLP uses.");
            }
        }
    }
}
