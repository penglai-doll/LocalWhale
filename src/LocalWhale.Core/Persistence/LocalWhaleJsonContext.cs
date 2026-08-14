using System.Text.Json.Serialization;
using LocalWhale.Core.Models;
using LocalWhale.Core.Runtime;
using LocalWhale.Core.Updates;

namespace LocalWhale.Core.Persistence;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(RuntimeState))]
[JsonSerializable(typeof(RuntimeManifest))]
[JsonSerializable(typeof(BridgeHealth))]
[JsonSerializable(typeof(RuntimePackageJson))]
public sealed partial class LocalWhaleJsonContext : JsonSerializerContext;
