namespace CpcPrototype.Core
{
    public enum AmmoReloadResult
    {
        Reloaded,
        AlreadyFull,
        NoReserveAmmo
    }

    /// <summary>Engine-independent ammunition and reload rules.</summary>
    public sealed class AmmoInventory
    {
        private readonly int _magazineCapacity;

        public int LoadedAmmo { get; private set; }
        public int ReserveAmmo { get; private set; }
        public int TotalAmmo => LoadedAmmo + ReserveAmmo;
        public bool IsOutOfAmmo => TotalAmmo == 0;

        public AmmoInventory(int magazineCapacity, int loadedAmmo, int reserveAmmo)
        {
            _magazineCapacity = magazineCapacity < 1 ? 1 : magazineCapacity;
            int maximumLoaded = _magazineCapacity + 1;
            LoadedAmmo = Clamp(loadedAmmo, 0, maximumLoaded);
            ReserveAmmo = reserveAmmo < 0 ? 0 : reserveAmmo;
        }

        public bool TryFire()
        {
            if (LoadedAmmo <= 0) return false;
            LoadedAmmo--;
            return true;
        }

        public AmmoReloadResult TryReload()
        {
            if (LoadedAmmo >= _magazineCapacity + 1) return AmmoReloadResult.AlreadyFull;
            if (ReserveAmmo <= 0) return AmmoReloadResult.NoReserveAmmo;

            // An empty reload fills the magazine; otherwise keep one chambered round.
            int targetLoaded = LoadedAmmo == 0 ? _magazineCapacity : _magazineCapacity + 1;
            int transfer = targetLoaded - LoadedAmmo;
            if (transfer > ReserveAmmo) transfer = ReserveAmmo;
            LoadedAmmo += transfer;
            ReserveAmmo -= transfer;
            return AmmoReloadResult.Reloaded;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }
    }
}
