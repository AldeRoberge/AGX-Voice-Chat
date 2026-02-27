using System.Collections.Generic;

namespace AGH.Shared
{
    public class ExperienceRank(string name, int rankIndex)
    {
        public string Name { get; } = name;
        public int RankIndex { get; } = rankIndex;

        public static readonly ExperienceRank Newbie = new(nameof(Newbie), 0);
        public static readonly ExperienceRank Rookie = new(nameof(Rookie), 1);
        public static readonly ExperienceRank Novice = new(nameof(Novice), 2);
        public static readonly ExperienceRank Apprentice = new(nameof(Apprentice), 3);
        public static readonly ExperienceRank Adept = new(nameof(Adept), 4);
        public static readonly ExperienceRank Skilled = new(nameof(Skilled), 5);
        public static readonly ExperienceRank Gifted = new(nameof(Gifted), 6);
        public static readonly ExperienceRank Expert = new(nameof(Expert), 7);
        public static readonly ExperienceRank Prodigy = new(nameof(Prodigy), 8);
        public static readonly ExperienceRank Elite = new(nameof(Elite), 9);
        public static readonly ExperienceRank Master = new(nameof(Master), 10);
        public static readonly ExperienceRank Grandmaster = new(nameof(Grandmaster), 11);

        public static readonly List<ExperienceRank> All = new()
        {
            Newbie, Rookie, Novice, Apprentice, Adept, Skilled,
            Gifted, Expert, Prodigy, Elite, Master, Grandmaster
        };
    }
}