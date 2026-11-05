using System.Collections.Generic;
using DriftCareer.Core;
using DriftCareer.Core.Account;
using DriftCareer.Core.Character;
using DriftCareer.Core.Gameplay;
using DriftCareer.Core.Persistence;
using NUnit.Framework;

namespace DriftCareer.Tests.EditMode
{
    public class IdentityAndCharacterTests
    {
        [TestCase("Drifter", NicknameError.None)]
        [TestCase("Корч_Мастер", NicknameError.None)]
        [TestCase("ab", NicknameError.TooShort)]
        [TestCase("abcdefghijklmnopqrstuvwxyz", NicknameError.TooLong)]
        [TestCase("ivan@mail.ru", NicknameError.InvalidCharacters)]
        [TestCase("Ivan 89991234567", NicknameError.LooksLikePersonalData)]
        [TestCase("Admin_Drift", NicknameError.Reserved)]
        [TestCase("<script>", NicknameError.InvalidCharacters)]
        public void Nickname_Validation(string nick, NicknameError expected)
        {
            Assert.AreEqual(expected, PlayerIdentity.Validate(nick));
        }

        [Test]
        public void DefaultNickname_RevealsNothing_AndLevelGrows()
        {
            Assert.AreEqual("Drifter-4F2A", PlayerIdentity.DefaultFor("0000aaaa4f2a"));
            Assert.AreEqual(1, PlayerIdentity.Level(0, out float p0));
            Assert.AreEqual(0f, p0, 1e-6);
            Assert.AreEqual(2, PlayerIdentity.Level(5000, out _));
            int high = PlayerIdentity.Level(1e6, out float p);
            Assert.Greater(high, 10);
            Assert.That(p, Is.InRange(0f, 1f));
            Assert.AreEqual("KM", PlayerIdentity.Initials("Korch Master"));
        }

        [Test]
        public void Wallet_History_IsCappedAndOrdered()
        {
            var w = new Wallet();
            for (int i = 0; i < 100; i++) w.Earn(10, "wallet.reason.chain");
            Assert.IsTrue(w.Spend(300, "wallet.reason.parts"));
            Assert.AreEqual(Wallet.HistoryLimit, w.history.Count);
            Assert.AreEqual(-300, w.history[0].amount);
            Assert.AreEqual(700, w.history[0].balanceAfter);
            Assert.AreEqual(300, w.lifetimeSpent);
            Assert.IsTrue(w.Spend(0));
            Assert.AreEqual(-300, w.history[0].amount, "zero-price purchases are not logged");
        }

        [Test]
        public void Character_SanitizeRejectsBadData()
        {
            var a = new CharacterAppearance { height = 5f, skinTone = "pink", hairStyle = 99, customModelId = "../../evil" };
            a.gear[GearSlot.Head] = new GearChoice { itemId = "top.hoodie", primary = "#FFFFFF" };       // wrong slot
            a.gear[GearSlot.Top] = new GearChoice { itemId = "top.race_suit", primary = "nope", secondary = "#00FF00" };
            var s = CharacterCatalog.Sanitize(a);
            Assert.AreEqual(1.1f, s.height, 1e-6);
            Assert.IsTrue(Core.Garage.VisualPartCatalog.IsValidColor(s.skinTone));
            Assert.Less(s.hairStyle, CharacterCatalog.HairStyleCount);
            Assert.AreEqual("", s.customModelId);
            Assert.IsFalse(s.gear.ContainsKey(GearSlot.Head));
            Assert.AreEqual("top.race_suit", s.Gear(GearSlot.Top).itemId);
            Assert.IsTrue(Core.Garage.VisualPartCatalog.IsValidColor(s.Gear(GearSlot.Top).primary));
        }

        [Test]
        public void Character_PresetsAreValid_AndGearCostsCredits()
        {
            foreach (var id in CharacterCatalog.PresetIds)
            {
                var p = CharacterCatalog.Sanitize(CharacterCatalog.Preset(id));
                foreach (var kv in p.gear) Assert.IsNotNull(CharacterCatalog.Find(kv.Value.itemId), id);
            }
            var pro = CharacterCatalog.Preset("pro");
            var owned = new HashSet<string>();
            long cost = CharacterCatalog.MissingCost(pro, owned);
            Assert.Greater(cost, 5000);
            CharacterCatalog.MarkOwned(pro, owned);
            Assert.AreEqual(0, CharacterCatalog.MissingCost(pro, owned));
            Assert.AreEqual(0, CharacterCatalog.MissingCost(new CharacterAppearance(), new HashSet<string>()), "default look is free");
        }

        [Test]
        public void Profile_PersistsNicknameCharacterAndWalletHistory()
        {
            var fs = new MemoryFileStore();
            var store = new VersionedDocumentStore<PlayerProfile>(fs, new ProfileCodec());
            var p = new PlayerProfile { nickname = "Korch Master", character = CharacterCatalog.Preset("street") };
            p.wallet.Earn(500, "wallet.reason.drill");
            store.Save("p.json", p);
            var b = store.Load("p.json", out _);
            Assert.AreEqual("Korch Master", b.nickname);
            Assert.AreEqual("head.cap", b.character.Gear(GearSlot.Head).itemId);
            Assert.AreEqual(1, b.wallet.history.Count);
            Assert.AreEqual("wallet.reason.drill", b.wallet.history[0].reason);
        }
    }
}
