using Bd.Core.Config;
using Bd.Core.Domain;

namespace Bd.Core.Services;

/// <summary>created_by / 评论 author 与 human actor + allowlist 比对，只读派生，不写任何字段。</summary>
public sealed class AttributionService
{
    private AppConfig _config;

    public AttributionService(AppConfig config) => _config = config ?? throw new ArgumentNullException(nameof(config));

    public void Update(AppConfig config) => _config = config ?? throw new ArgumentNullException(nameof(config));

    public Attribution Resolve(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            return Attribution.Unknown;
        }

        return _config.IsHuman(actor, [])
            ? Attribution.Human(actor.Trim())
            : Attribution.Agent(actor.Trim());
    }

    public bool IsHuman(string? actor) => Resolve(actor).Kind == AttributionKind.Human;
}
