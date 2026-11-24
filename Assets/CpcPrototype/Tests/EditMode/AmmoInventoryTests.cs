using CpcPrototype.Core;
using NUnit.Framework;

namespace CpcPrototype.Tests
{
    public sealed class AmmoInventoryTests
    {
        [Test]
        public void Fire_ConsumesExactlyOneRound_AndCannotFireWhenEmpty()
        {
            var ammo = new AmmoInventory(30, 2, 5);

            Assert.IsTrue(ammo.TryFire());
            Assert.AreEqual(1, ammo.LoadedAmmo);
            Assert.IsTrue(ammo.TryFire());
            Assert.AreEqual(0, ammo.LoadedAmmo);
            Assert.IsFalse(ammo.TryFire());
            Assert.AreEqual(5, ammo.TotalAmmo);
        }

        [Test]
        public void Reload_FromEmptyLoadsAtMostMagazineCapacity()
        {
            var ammo = new AmmoInventory(30, 0, 40);

            Assert.AreEqual(AmmoReloadResult.Reloaded, ammo.TryReload());
            Assert.AreEqual(30, ammo.LoadedAmmo);
            Assert.AreEqual(10, ammo.ReserveAmmo);
        }

        [Test]
        public void Reload_AfterLastRoundUsesEmptyMagazineCapacity()
        {
            var ammo = new AmmoInventory(30, 1, 40);

            Assert.IsTrue(ammo.TryFire());
            Assert.AreEqual(0, ammo.LoadedAmmo);
            Assert.AreEqual(AmmoReloadResult.Reloaded, ammo.TryReload());
            Assert.AreEqual(30, ammo.LoadedAmmo);
            Assert.AreEqual(10, ammo.ReserveAmmo);
        }

        [Test]
        public void Reload_WithRoundInMagazineLoadsOneChamberedRound()
        {
            var ammo = new AmmoInventory(30, 4, 40);

            Assert.AreEqual(AmmoReloadResult.Reloaded, ammo.TryReload());
            Assert.AreEqual(31, ammo.LoadedAmmo);
            Assert.AreEqual(13, ammo.ReserveAmmo);
        }

        [Test]
        public void Reload_UsesOnlyRoundsAvailableInReserve()
        {
            var ammo = new AmmoInventory(30, 4, 2);

            Assert.AreEqual(AmmoReloadResult.Reloaded, ammo.TryReload());
            Assert.AreEqual(6, ammo.LoadedAmmo);
            Assert.AreEqual(0, ammo.ReserveAmmo);
        }

        [Test]
        public void Reload_ReportsFullMagazineAndEmptyReserve()
        {
            var full = new AmmoInventory(30, 31, 10);
            var empty = new AmmoInventory(30, 0, 0);

            Assert.AreEqual(AmmoReloadResult.AlreadyFull, full.TryReload());
            Assert.AreEqual(AmmoReloadResult.NoReserveAmmo, empty.TryReload());
            Assert.IsTrue(empty.IsOutOfAmmo);
        }

        [Test]
        public void Constructor_ClampsInvalidInitialValues()
        {
            var ammo = new AmmoInventory(30, 80, -5);

            Assert.AreEqual(31, ammo.LoadedAmmo);
            Assert.AreEqual(0, ammo.ReserveAmmo);
        }
    }
}
