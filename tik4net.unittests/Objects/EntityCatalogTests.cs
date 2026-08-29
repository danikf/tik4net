using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// Keeps the published entity catalog honest: the table a reader uses to answer "is my menu covered?"
    /// is generated from the <c>[TikEntity]</c> attributes, and this fails the build when the tracked copy
    /// no longer matches what the code declares.
    /// </summary>
    /// <remarks>
    /// Without this the page is a list somebody remembered to update. With it, adding an entity and not
    /// publishing it is a red build — which is the only version of "documented coverage" that stays true.
    /// <para>
    /// The tracked copy is <c>Docs/entity-catalog.md</c>. Set <c>TIK4NET_UPDATE_DOCS=1</c> and re-run to
    /// rewrite it, then paste the block into the wiki's <i>Entity reference</i> page between its generated
    /// markers. That last step is manual because the wiki is a separate repository which CI does not have.
    /// </para>
    /// </remarks>
    [TestClass]
    public class EntityCatalogTests
    {
        private const string CatalogRelativePath = "Docs/entity-catalog.md";

        [TestMethod]
        public void TheTrackedCatalogMatchesWhatTheEntitiesDeclare()
        {
            string generated = EntityCatalogMarkdown.Build();
            string path = ResolveRepoFile(CatalogRelativePath);

            if (Environment.GetEnvironmentVariable("TIK4NET_UPDATE_DOCS") == "1")
            {
                File.WriteAllText(path, generated);
                Assert.Inconclusive("TIK4NET_UPDATE_DOCS=1 - rewrote " + CatalogRelativePath
                    + ". Re-run without it, and paste the block into the wiki's Entity-reference page.");
            }

            string tracked = File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.AreEqual(tracked, generated,
                "The entity catalog is out of date. Re-run this test with TIK4NET_UPDATE_DOCS=1 to "
                + "regenerate " + CatalogRelativePath + ", then paste the block into the wiki's "
                + "Entity-reference page between its generated markers.");
        }

        /// <summary>
        /// Holds the naming convention the catalog's "has helpers" marker depends on: a helper class is
        /// named after the entity it serves.
        /// </summary>
        /// <remarks>
        /// This is the half that catches the mistake nobody would otherwise see. A helper class named after
        /// the wrong entity still compiles and still works; it just makes its verbs invisible in the
        /// catalog, which is exactly how <c>FlushDnsCache</c> came to live in a class called
        /// <c>AccountingSnapshotConnectionExtensions</c>.
        /// </remarks>
        [TestMethod]
        public void EveryHelperClassNamesItsEntity()
        {
            var entityNames = EntityCatalogMarkdown.EntityTypes()
                .Select(t => t.Name)
                .ToHashSet(StringComparer.Ordinal);

            var orphans = EntityCatalogMarkdown.HelperClasses()
                .Where(kv => !entityNames.Contains(kv.Key))
                .Select(kv => kv.Value.Name + " -> no entity named " + kv.Key)
                .ToList();

            Assert.AreEqual(0, orphans.Count,
                "A *ConnectionExtensions class must be named after the entity it serves, so its verbs "
                + "appear against that entity in the catalog: " + string.Join("; ", orphans));
        }

        /// <summary>
        /// Entity paths are spelled one way. 42 of them used to omit the leading slash, which the mapper
        /// tolerates but which makes the catalog sort into two groups and reads as two conventions.
        /// </summary>
        [TestMethod]
        public void EveryEntityPathStartsWithASlash()
        {
            var offenders = EntityCatalogMarkdown.EntityTypes()
                .Select(t => new
                {
                    t.Name,
                    Path = t.GetCustomAttributes(typeof(TikEntityAttribute), false)
                            .Cast<TikEntityAttribute>().Single().EntityPath,
                })
                .Where(x => !x.Path.StartsWith("/", StringComparison.Ordinal))
                .Select(x => x.Name + " = \"" + x.Path + "\"")
                .ToList();

            Assert.AreEqual(0, offenders.Count,
                "[TikEntity] paths start with '/': " + string.Join("; ", offenders));
        }

        /// <summary>Walks up from the test binary to the repository root.</summary>
        private static string ResolveRepoFile(string relativePath)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "tik4net.sln")))
                dir = dir.Parent;

            Assert.IsNotNull(dir, "Could not find the repository root (no tik4net.sln above "
                                  + AppDomain.CurrentDomain.BaseDirectory + ").");
            return Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
