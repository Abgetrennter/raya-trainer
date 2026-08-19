using System.Text.Json;

namespace RayaTrainer.Core.Agent;

public sealed record DirectGameApiCatalog(
    int Version,
    IReadOnlyList<DirectGameApiDefinition> Apis)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static DirectGameApiCatalog Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var dto = JsonSerializer.Deserialize<CatalogDto>(stream, JsonOptions)
            ?? throw new InvalidDataException("Direct GameApi catalog is empty.");
        if (dto.Version != 3)
        {
            throw new InvalidDataException($"Direct GameApi catalog version must be 3, actual {dto.Version}.");
        }

        if (dto.Apis is null || dto.Apis.Count == 0)
        {
            throw new InvalidDataException("Direct GameApi catalog must define at least one API.");
        }

        return new DirectGameApiCatalog(dto.Version, dto.Apis.Select(ToDefinition).ToArray());
    }

    private static DirectGameApiDefinition ToDefinition(ApiDto dto)
    {
        var name = Required(dto.Name, "api.name");
        return new DirectGameApiDefinition(
            name,
            dto.Description,
            ParseImplementation(dto.Implementation, name),
            Required(dto.RequestType, $"{name}.requestType"),
            Required(dto.PayloadType, $"{name}.payloadType"),
            ToPipeCommand(name, dto.PipeCommand),
            (dto.RequestFields ?? []).Select(field => ToField(name, field)).ToArray(),
            (dto.PayloadFields ?? []).Select(field => ToField(name, field)).ToArray(),
            ToFeatureMethod(name, dto.FeatureMethod));
    }

    private static DirectGameApiImplementation ParseImplementation(string? value, string apiName)
    {
        if (Enum.TryParse<DirectGameApiImplementation>(value, ignoreCase: true, out var implementation))
        {
            return implementation;
        }

        throw new InvalidDataException($"{apiName}.implementation must be 'native'.");
    }

    private static DirectGameApiPipeCommand ToPipeCommand(string apiName, PipeCommandDto? dto)
    {
        if (dto is null)
        {
            throw new InvalidDataException($"{apiName}.pipeCommand is required.");
        }

        return new DirectGameApiPipeCommand(
            Required(dto.Name, $"{apiName}.pipeCommand.name"),
            dto.Value,
            Required(dto.ClientMethod, $"{apiName}.pipeCommand.clientMethod"));
    }

    private static DirectGameApiField ToField(string apiName, FieldDto dto)
    {
        return new DirectGameApiField(
            Required(dto.Type, $"{apiName}.field.type"),
            Required(dto.Name, $"{apiName}.field.name"),
            dto.RequiredNonZero);
    }

    private static DirectGameApiFeatureMethod ToFeatureMethod(string apiName, FeatureMethodDto? dto)
    {
        if (dto is null)
        {
            throw new InvalidDataException($"{apiName}.featureMethod is required.");
        }

        return new DirectGameApiFeatureMethod(
            Required(dto.Name, $"{apiName}.featureMethod.name"),
            Required(dto.ReturnType, $"{apiName}.featureMethod.returnType"),
            (dto.Parameters ?? []).Select(parameter => ToFeatureParameter(apiName, parameter)).ToArray(),
            dto.ReturnField,
            dto.DocSummary,
            dto.DocReturn,
            dto.Generate ?? true);
    }

    private static DirectGameApiFeatureParameter ToFeatureParameter(string apiName, FeatureParameterDto dto)
    {
        return new DirectGameApiFeatureParameter(
            Required(dto.Type, $"{apiName}.featureParameter.type"),
            Required(dto.Name, $"{apiName}.featureParameter.name"),
            dto.MapsTo,
            dto.Default);
    }

    private static string Required(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Direct GameApi catalog field {field} is required.");
        }

        return value;
    }

    private sealed record CatalogDto
    {
        public int Version { get; init; }

        public List<ApiDto>? Apis { get; init; }
    }

    private sealed record ApiDto
    {
        public string? Name { get; init; }

        public string? Description { get; init; }

        public string? Implementation { get; init; }

        public string? RequestType { get; init; }

        public string? PayloadType { get; init; }

        public PipeCommandDto? PipeCommand { get; init; }

        public List<FieldDto>? RequestFields { get; init; }

        public List<FieldDto>? PayloadFields { get; init; }

        public FeatureMethodDto? FeatureMethod { get; init; }
    }

    private sealed record PipeCommandDto
    {
        public string? Name { get; init; }

        public int Value { get; init; }

        public string? ClientMethod { get; init; }
    }

    private sealed record FieldDto
    {
        public string? Type { get; init; }

        public string? Name { get; init; }

        public bool RequiredNonZero { get; init; }
    }

    private sealed record FeatureMethodDto
    {
        public string? Name { get; init; }

        public string? ReturnType { get; init; }

        public List<FeatureParameterDto>? Parameters { get; init; }

        public string? ReturnField { get; init; }

        public string? DocSummary { get; init; }

        public string? DocReturn { get; init; }

        public bool? Generate { get; init; }
    }

    private sealed record FeatureParameterDto
    {
        public string? Type { get; init; }

        public string? Name { get; init; }

        public string? MapsTo { get; init; }

        public string? Default { get; init; }
    }
}

public enum DirectGameApiImplementation
{
    Native
}

public sealed record DirectGameApiDefinition(
    string Name,
    string? Description,
    DirectGameApiImplementation Implementation,
    string RequestType,
    string PayloadType,
    DirectGameApiPipeCommand PipeCommand,
    IReadOnlyList<DirectGameApiField> RequestFields,
    IReadOnlyList<DirectGameApiField> PayloadFields,
    DirectGameApiFeatureMethod FeatureMethod);

public sealed record DirectGameApiPipeCommand(
    string Name,
    int Value,
    string ClientMethod);

public sealed record DirectGameApiField(
    string Type,
    string Name,
    bool RequiredNonZero = false);

public sealed record DirectGameApiFeatureMethod(
    string Name,
    string ReturnType,
    IReadOnlyList<DirectGameApiFeatureParameter> Parameters,
    string? ReturnField,
    string? DocSummary,
    string? DocReturn,
    bool Generate = true);

public sealed record DirectGameApiFeatureParameter(
    string Type,
    string Name,
    string? MapsTo,
    string? Default);
