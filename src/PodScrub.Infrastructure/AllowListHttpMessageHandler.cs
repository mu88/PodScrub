using Microsoft.Extensions.Logging;

namespace PodScrub.Infrastructure;

public class AllowListHttpMessageHandler : DelegatingHandler
{
    private readonly HashSet<string> _allowedHosts;
    private readonly ILogger<AllowListHttpMessageHandler> _logger;

    public AllowListHttpMessageHandler(IEnumerable<string> allowedFeedUrls, ILogger<AllowListHttpMessageHandler> logger)
        : this(allowedFeedUrls, logger, new HttpClientHandler())
    {
    }

    public AllowListHttpMessageHandler(IEnumerable<string> allowedFeedUrls, ILogger<AllowListHttpMessageHandler> logger, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _logger = logger;
        _allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var url in allowedFeedUrls)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                _allowedHosts.Add(uri.Host);
            }
        }
    }

    internal IReadOnlyCollection<string> AllowedHosts => _allowedHosts;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is null)
        {
            throw new InvalidOperationException("Request URI must not be null.");
        }

        if (!IsHostAllowed(request.RequestUri.Host))
        {
            _logger.LogWarning("Blocked outbound request to {Host} — not in allow list", request.RequestUri.Host);
            throw new InvalidOperationException($"Outbound HTTP requests to '{request.RequestUri.Host}' are not allowed. Only configured feed hosts are permitted.");
        }

        return base.SendAsync(request, cancellationToken);
    }

    internal bool IsHostAllowed(string host)
    {
        if (_allowedHosts.Contains(host))
        {
            return true;
        }

        return _allowedHosts.Any(allowedHost => host.EndsWith($".{allowedHost}", StringComparison.OrdinalIgnoreCase));
    }
}
