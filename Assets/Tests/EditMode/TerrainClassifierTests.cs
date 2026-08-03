using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ScrapSiege.Terrain;

namespace ScrapSiege.Tests
{
    /// <summary>
    /// Locks in plan.md's Mechanic 1 classification table so future threshold tuning can't
    /// silently flip an archetype without a test failing. Pure logic, no scene/MonoBehaviour
    /// dependency - TerrainObjectData/TerrainClassifier are plain C#.
    /// </summary>
    public class TerrainClassifierTests
    {
        private static TerrainObjectData MakeObject(float sizeX, float sizeZ, HeightCategory height)
        {
            return new TerrainObjectData
            {
                CornerA = Vector3.zero,
                CornerB = new Vector3(sizeX, 0f, sizeZ),
                Height = height
            };
        }

        [Test]
        public void ElongatedFootprint_ClassifiesAsWallBarricade_RegardlessOfHeight()
        {
            var data = MakeObject(1f, 0.1f, HeightCategory.Medium);

            Assert.AreEqual(TerrainArchetype.WallBarricade, TerrainClassifier.Classify(data));
        }

        [Test]
        public void TallNonElongatedFootprint_ClassifiesAsSpireChokepoint()
        {
            var data = MakeObject(0.05f, 0.05f, HeightCategory.Tall);

            Assert.AreEqual(TerrainArchetype.SpireChokepoint, TerrainClassifier.Classify(data));
        }

        [Test]
        public void ShortWideFootprint_ClassifiesAsRubbleCover()
        {
            var data = MakeObject(0.3f, 0.3f, HeightCategory.Short);

            Assert.AreEqual(TerrainArchetype.RubbleCover, TerrainClassifier.Classify(data));
        }

        [Test]
        public void ShortSmallFootprint_FallsBelowCoverThreshold_ClassifiesAsPlainObstacle()
        {
            var data = MakeObject(0.1f, 0.1f, HeightCategory.Short);

            Assert.AreEqual(TerrainArchetype.PlainObstacle, TerrainClassifier.Classify(data));
        }

        [Test]
        public void MediumNonElongatedFootprint_ClassifiesAsPlainObstacle()
        {
            var data = MakeObject(0.2f, 0.2f, HeightCategory.Medium);

            Assert.AreEqual(TerrainArchetype.PlainObstacle, TerrainClassifier.Classify(data));
        }

        [Test]
        public void WatchtowerOverride_PicksLargestOfTheTallObjects()
        {
            var smallSpire = MakeObject(0.05f, 0.05f, HeightCategory.Tall);
            var largeSpire = MakeObject(0.15f, 0.15f, HeightCategory.Tall);
            var shortObject = MakeObject(0.3f, 0.3f, HeightCategory.Short);

            smallSpire.Archetype = TerrainClassifier.Classify(smallSpire);
            largeSpire.Archetype = TerrainClassifier.Classify(largeSpire);
            shortObject.Archetype = TerrainClassifier.Classify(shortObject);

            var objects = new List<TerrainObjectData> { smallSpire, largeSpire, shortObject };
            TerrainClassifier.ApplyWatchtowerOverride(objects);

            Assert.AreEqual(TerrainArchetype.Watchtower, largeSpire.Archetype);
            Assert.AreEqual(TerrainArchetype.SpireChokepoint, smallSpire.Archetype);
            Assert.AreEqual(TerrainArchetype.RubbleCover, shortObject.Archetype);
        }

        [Test]
        public void WatchtowerOverride_NoTallObjects_LeavesArchetypesUnchanged()
        {
            var shortObject = MakeObject(0.3f, 0.3f, HeightCategory.Short);
            shortObject.Archetype = TerrainClassifier.Classify(shortObject);

            var objects = new List<TerrainObjectData> { shortObject };
            TerrainClassifier.ApplyWatchtowerOverride(objects);

            Assert.AreEqual(TerrainArchetype.RubbleCover, shortObject.Archetype);
        }
    }
}
