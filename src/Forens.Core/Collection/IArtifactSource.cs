namespace Forens.Core.Collection
{
    public interface IArtifactSource
    {
        SourceMetadata Metadata { get; }

        SourcePrecondition CheckPrecondition(CollectionContext ctx);

        void Collect(CollectionContext ctx, ISourceWriter writer);
    }
}
