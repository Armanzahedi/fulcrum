namespace Fulcrum;

internal static class Endpoint
{
    public static Uri Resolve(string provider, Uri baseAddress, string relative)
    {
        if (!baseAddress.IsAbsoluteUri || baseAddress.Query.Length > 0 || baseAddress.Fragment.Length > 0)
            throw new FulcrumException($"{provider}: BaseAddress must be an absolute URI without a query or fragment");
        var withSlash = baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri(baseAddress.AbsoluteUri + "/");
        return new Uri(withSlash, relative);
    }

    public static string? ApiKey(string? configured, Uri baseAddress, string defaultHost, string environmentVariable) =>
        !string.IsNullOrWhiteSpace(configured) ? configured
        : baseAddress.IsAbsoluteUri && baseAddress.Host == defaultHost ? Environment.GetEnvironmentVariable(environmentVariable)
        : null;
}
