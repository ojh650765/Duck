"""DUCK MOW - Noise/ utility textures. See Docs/TEXTURE_SPEC.md."""
import numpy as np
from PIL import Image
import duckart as D


# ======================================================================================
def perlin_rgba(size=512):
    """Four decorrelated fbm octave-bands, one per channel, all periodic.
      R = 2 m mottling  (base freq 4)   G = 0.9 m  (8)
      B = 0.4 m         (16)            A = 0.15 m (32)
    Different seeds per channel so the channels are decorrelated, not just scaled."""
    ch = []
    for k, (bf, oc, per, seed) in enumerate([(4, 4, 0.52, 101), (8, 4, 0.5, 3307),
                                             (16, 3, 0.5, 5501), (32, 3, 0.48, 9109)]):
        n = D.fbm(size, bf, oc, per, 2, seed)
        # gentle domain warp on the two lower bands keeps them from looking like
        # symmetric blob soup
        if k < 2:
            wx = D.perlin(size, bf * 2, seed + 17)
            wy = D.perlin(size, bf * 2, seed + 23)
            n = D.warp(n, wx, wy, size / (bf * 12))
        ch.append(D.normalise(n, 0.03, 0.97))
    a = np.stack(ch, -1)
    # zero-mean-ish per channel so shaders can use (x*2-1) safely
    for i in range(4):
        a[..., i] = np.clip(a[..., i] + (0.5 - a[..., i].mean()), 0, 1)
    D.save(D.outpath("Noise", "noise_perlin_rgba_512.png"), a)
    corr = np.corrcoef(a.reshape(-1, 4).T)
    print("perlin rgba channel correlation (off-diag max): %.3f"
          % np.abs(corr - np.eye(4)).max())


# ======================================================================================
def worley_rgba(size=512, cells=8):
    d, cid = D.worley(size, cells, seed=771, n=2, jitter=0.95)
    f1 = np.clip(d[0] / 0.9, 0, 1)
    f2f1 = np.clip((d[1] - d[0]) / 0.75, 0, 1)          # cell-edge mask
    fine, _ = D.worley(size, cells * 4, seed=4409, n=1, jitter=1.0)
    fine = np.clip(fine[0] / 0.85, 0, 1)
    a = np.stack([f1, f2f1, cid, fine], -1)
    D.save(D.outpath("Noise", "noise_worley_512.png"), a)


# ======================================================================================
def blue_noise(size=256, sigma=1.9, seed=12345):
    """Void-and-cluster (Ulichney 1993), toroidal. Produces a true blue-noise
    threshold/dither array."""
    N = size * size
    yy, xx = np.mgrid[0:size, 0:size]
    dy = np.minimum(yy, size - yy)
    dx = np.minimum(xx, size - xx)
    K = np.exp(-(dx ** 2 + dy ** 2) / (2 * sigma ** 2))
    Kf = np.fft.rfft2(K)

    r = int(np.ceil(sigma * 4))
    off = np.arange(-r, r + 1)
    Kp = np.exp(-(off[None, :] ** 2 + off[:, None] ** 2) / (2 * sigma ** 2))

    def full_energy(p):
        return np.fft.irfft2(np.fft.rfft2(p.astype(np.float64)) * Kf, s=(size, size))

    def patch(e, y, x, s):
        iy = (y + off) % size
        ix = (x + off) % size
        e[np.ix_(iy, ix)] += s * Kp

    rng = np.random.default_rng(seed)
    M = N // 10
    pattern = np.zeros((size, size), np.uint8)
    idx = rng.permutation(N)[:M]
    pattern.flat[idx] = 1
    E = full_energy(pattern)

    NEG, POS = -1e18, 1e18
    for it in range(20000):
        e = np.where(pattern == 1, E, NEG)
        c = int(e.argmax()); cy, cx = divmod(c, size)
        pattern[cy, cx] = 0; patch(E, cy, cx, -1.0)
        e = np.where(pattern == 0, E, POS)
        v = int(e.argmin()); vy, vx = divmod(v, size)
        pattern[vy, vx] = 1; patch(E, vy, vx, 1.0)
        if (vy, vx) == (cy, cx):
            break
    print("  void-and-cluster converged in %d swaps" % it)

    initial = pattern.copy()
    rank = np.full((size, size), -1, np.int64)

    # phase 1 - rank the initial points downward by removing tightest clusters
    p = initial.copy()
    E = full_energy(p)
    for k in range(M - 1, -1, -1):
        e = np.where(p == 1, E, NEG)
        i = int(e.argmax()); y, x = divmod(i, size)
        p[y, x] = 0; patch(E, y, x, -1.0)
        rank[y, x] = k

    # phase 2 - fill the largest voids upward to half
    p = initial.copy()
    E = full_energy(p)
    for k in range(M, N // 2):
        e = np.where(p == 0, E, POS)
        i = int(e.argmin()); y, x = divmod(i, size)
        p[y, x] = 1; patch(E, y, x, 1.0)
        rank[y, x] = k

    # phase 3 - swap roles: remove the tightest clusters of the *minority* zeros
    Ez = full_energy(1 - p)
    for k in range(N // 2, N):
        e = np.where(p == 0, Ez, NEG)
        i = int(e.argmax()); y, x = divmod(i, size)
        p[y, x] = 1; patch(Ez, y, x, -1.0)
        rank[y, x] = k

    assert (rank >= 0).all()
    bn = rank.astype(np.float64) / (N - 1)
    D.save(D.outpath("Noise", "noise_blue_256.png"), bn)
    return bn


def verify_blue(path=None):
    """Radially averaged power spectrum. Blue noise -> near-zero power at low
    frequency, flat/raised at high frequency, no spikes."""
    p = path or D.outpath("Noise", "noise_blue_256.png")
    a = D.load(p)
    a = a - a.mean()
    P = np.abs(np.fft.fftshift(np.fft.fft2(a))) ** 2
    s = a.shape[0]
    yy, xx = np.mgrid[0:s, 0:s] - s / 2
    rr = np.sqrt(xx ** 2 + yy ** 2).astype(int)
    prof = np.bincount(rr.ravel(), P.ravel()) / np.maximum(1, np.bincount(rr.ravel()))
    prof = prof[:s // 2] / prof[1:s // 2].mean()
    print("  radial power (normalised) at r = 1,2,4,8,16,32,64,127:")
    print("   ", " ".join("%.3f" % prof[i] for i in [1, 2, 4, 8, 16, 32, 64, 127]))
    lowf = prof[1:16].mean(); highf = prof[48:120].mean()
    print("  low(1-16)=%.3f  high(48-120)=%.3f  ratio=%.4f  %s"
          % (lowf, highf, lowf / highf, "BLUE OK" if lowf / highf < 0.25 else "NOT BLUE"))
    # spectrum image for eyeballing
    img = np.log1p(P / P.max() * 4000)
    D.save(D.PREVIEW + r"\blue_noise_fft.png", D.normalise(img))


# ======================================================================================
def grass_blade(size=128):
    """Tapered blade with a soft tip, slight S-curve, on transparent.
    RGB = along-blade value ramp (root .70 -> tip 1.0) so a card shader can lerp
    uncut_base -> uncut_tip with it. A = coverage."""
    ss = 4
    S = size * ss
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float64)
    v = 1.0 - yy / (S - 1)            # 0 at bottom(root) .. 1 at top(tip)

    # centreline: gentle S bend, root planted at centre bottom
    cx = 0.5 - 0.075 * v ** 1.6 + 0.045 * np.sin(v * 3.4)
    # half-width: full at the root, a slight bulge at ~20% up, then a long taper
    w = 0.125 * (1.0 + 0.16 * np.sin(v * 3.1)) * (1.0 - v ** 1.9) ** 0.55
    w = np.clip(w, 1e-4, 1)
    dist = np.abs(xx / (S - 1) - cx) / w
    soft = 0.055 + 0.30 * v ** 3            # tip is softer than the root
    a = np.clip((1.0 - dist) / soft, 0, 1)
    a = a ** 0.85
    a[v > 0.995] = 0
    a[v < 0.004] *= 0.6

    # a shallow centre rib brightens the middle of the blade
    rib = np.exp(-(dist / 0.42) ** 2) * 0.10
    val = (0.70 + 0.30 * v ** 0.8 + rib)

    img = np.dstack([val, val, val, a])
    im = Image.fromarray(D.to8(np.clip(img, 0, 1)), "RGBA").resize((size, size), Image.LANCZOS)
    im.save(D.outpath("Noise", "grass_blade_alpha_128.png"), optimize=True)


if __name__ == "__main__":
    print("perlin rgba...");  perlin_rgba()
    print("worley...");       worley_rgba()
    print("blue noise...");   blue_noise(); verify_blue()
    print("grass blade...");  grass_blade()
    print("done")
