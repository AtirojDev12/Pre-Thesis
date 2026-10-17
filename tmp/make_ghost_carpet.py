from pathlib import Path
import json
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/Sound/GhostWalkSound'
PREVIEW = ROOT / 'tmp/GhostFootsteps'
OUT.mkdir(parents=True, exist_ok=True)
PREVIEW.mkdir(parents=True, exist_ok=True)
SR = 48000
rng = np.random.default_rng(170826)

def read(name):
    with wave.open(str(ROOT / 'Assets/Sound/PlayerWalkSound' / name), 'rb') as w:
        rate = w.getframerate()
        a = np.frombuffer(w.readframes(w.getnframes()), '<i2').reshape(-1, w.getnchannels()).mean(axis=1) / 32768
    active = np.flatnonzero(np.abs(a) > np.max(np.abs(a)) * .025)
    a = a[max(0, active[0] - int(.012 * rate)):min(len(a), active[-1] + int(.055 * rate))]
    return a, rate

def filter_audio(a, low, high):
    freq = np.fft.rfftfreq(len(a), 1 / SR)
    weight = (1 - np.exp(-(freq / low) ** 4)) * np.exp(-(freq / high) ** 4)
    return np.fft.irfft(np.fft.rfft(a) * weight, n=len(a))

def stretch(a, rate, speed):
    length = round(len(a) * SR / rate / speed)
    return np.interp(np.arange(length) * speed * rate / SR, np.arange(len(a)), a)

def add(dst, src, time, gain=1):
    start = round(time * SR)
    count = min(len(src), len(dst) - start)
    if count > 0:
        dst[start:start+count] += src[:count] * gain

def noise(length, low, high):
    a = filter_audio(rng.normal(size=length), low, high)
    return a / (np.sqrt(np.mean(a*a)) + 1e-10)

def write(path, a):
    # TPDF dither before PCM16 conversion.
    dither = (rng.random(a.shape) - rng.random(a.shape)) / 65536
    pcm = np.rint(np.clip(a + dither, -.9999, .9999) * 32767).astype('<i2')
    with wave.open(str(path), 'wb') as w:
        w.setnchannels(1 if a.ndim == 1 else a.shape[1])
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())

configs = [
    ('Ghost_Carpet_01_HeavyPlant.wav', 'CarpetNoise1.wav', .73, 56, .115, .078, .12, 1.03),
    ('Ghost_Carpet_02_HeelRoll.wav', 'CarpetNoise4.wav', .68, 49, .145, .113, .18, 1.10),
    ('Ghost_Carpet_03_DragPlant.wav', 'Carpetnoise3.wav', .62, 62, .125, .091, .32, 1.18),
    ('Ghost_Carpet_04_DeepStamp.wav', 'CarpetNoise2.wav', .77, 43, .170, .066, .15, 1.14),
]
clips = []
report = []
for index, (name, source, speed, bass, decay, roll_delay, drag, duration) in enumerate(configs):
    x = np.zeros(round(duration * SR))
    a, rate = read(source)
    carpet = filter_audio(stretch(a, rate, speed), 75, 2300)
    carpet /= np.max(np.abs(carpet)) + 1e-10
    carpet *= np.minimum(np.arange(len(carpet)) / (SR * .008), 1)
    carpet *= np.minimum(np.arange(len(carpet))[::-1] / (SR * .028), 1)
    add(x, carpet, .012, .36)
    t = np.arange(round(.75 * SR)) / SR
    # A damped, irregular floor/body impact, supported by broad low-mid energy.
    phase = 2*np.pi*(bass*t + 9*.035*(1-np.exp(-t/.035)))
    envelope = (1-np.exp(-t/.0045))**2 * np.exp(-t/decay)
    thud = (.38*np.sin(phase) + .16*noise(len(t), 32, 180) + .10*noise(len(t), 100, 480)) * envelope
    add(x, thud, .014)
    roll = noise(len(t), 75, 630) * (1-np.exp(-t/.009))**2 * np.exp(-t/.072)
    add(x, roll, roll_delay, .14 if index != 3 else .19)
    # Compressing and releasing carpet fibers, with a different texture per take.
    s = np.arange(round(drag * SR)) / SR
    scuff = noise(len(s), 190, 1900) * np.sin(np.pi*np.arange(len(s))/len(s))**1.7
    modulation = .65+.35*np.sin(2*np.pi*(11+index*3)*s + index)
    add(x, scuff*modulation, .075+index*.012, .022 if index != 2 else .043)
    # Subtle short room response. Keep spatial positioning available to the game.
    dry = x.copy()
    for delay, gain in [(.027,.065),(.049,.037),(.083,.018)]:
        add(x, filter_audio(dry, 45, 950), delay, gain)
    x = filter_audio(x, 28, 5200)
    x *= np.minimum(np.arange(len(x))/(SR*.004),1)
    x *= np.minimum(np.arange(len(x))[::-1]/(SR*.10),1)
    # Common gain preserves intentional differences in the four performances.
    clips.append(x)

gain = .84 / max(np.max(np.abs(x)) for x in clips)
clips = [x * gain for x in clips]
for (name, *rest), x in zip(configs, clips):
    write(OUT / name, x)
    report.append({'file': name, 'duration_seconds': len(x)/SR, 'peak_dbfs': round(20*np.log10(np.max(np.abs(x))),2), 'rms_dbfs': round(20*np.log10(np.sqrt(np.mean(x*x))),2), 'finite': bool(np.isfinite(x).all())})

# A demonstration, with irregular pacing, weighted left/right motion and pauses.
order = [0,1,2,3,1,0,3,2,0,2,1,3,2,0]
gaps = [.93,1.12,.87,1.34,1.05,.96,1.53,1.09,.89,1.26,1.04,1.44,1.13]
positions = [.20]
for gap in gaps:
    positions.append(positions[-1]+gap)
demo = np.zeros((round((positions[-1]+1.6)*SR),2))
for n, (idx, pos) in enumerate(zip(order, positions)):
    a = clips[idx]
    speed = [.97,1.025,1.0,1.04,.985][n%5]
    a = np.interp(np.arange(round(len(a)/speed))*speed, np.arange(len(a)), a)
    pan = -.12 if n%2==0 else .12
    for ch, balance in enumerate([np.sqrt((1-pan)/2),np.sqrt((1+pan)/2)]):
        add(demo[:,ch], a, pos, balance * [.93,1,.88,.97][n%4])
write(PREVIEW/'Ghost_Carpet_Walk_Preview.wav', demo)
assert max(np.max(np.abs(x)) for x in clips) < 1
assert all(np.isfinite(x).all() and abs(x[0]) < 1e-7 and abs(x[-1]) < 1e-7 for x in clips)
assert len(set(x.tobytes() for x in clips)) == 4
(PREVIEW/'audio_check.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
print('Preview:', PREVIEW/'Ghost_Carpet_Walk_Preview.wav')
