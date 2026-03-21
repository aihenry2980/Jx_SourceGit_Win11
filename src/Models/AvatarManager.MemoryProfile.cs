namespace SourceGit.Models
{
    public partial class AvatarManager
    {
        public SharedMemoryProfile BuildMemoryProfile()
        {
            long bytes = 0;
            foreach (var entry in _resources.Values)
                bytes += MemoryProfileEstimator.EstimateBitmap(entry.Image) + 96;

            bytes += MemoryProfileEstimator.EstimateListReferences(_avatars);
            bytes += MemoryProfileEstimator.EstimateListReferences(_resources);
            bytes += MemoryProfileEstimator.EstimateListReferences(_requesting);

            return new SharedMemoryProfile(
                "Avatar cache",
                bytes,
                $"{_resources.Count} cached, {_requesting.Count} pending, {_defaultAvatars.Count} pinned defaults",
                "Shared across all repositories. If this stays small, history/graph state is the more likely source.");
        }
    }
}
