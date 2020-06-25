// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics.CodeAnalysis;
using osu.Server.QueueProcessor;

namespace osu.Server.Queues.BeatmapProcessor
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class BeatmapItem : QueueItem
    {
        public long beatmapset_id { get; set; }
    }
}
