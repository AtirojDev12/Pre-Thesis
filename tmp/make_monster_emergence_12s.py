"""Original layered sound design, 12-second cinematic emergence cue."""
from pathlib import Path
import wave
import json
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/Sound/GhostEmergence'
OUT.mkdir(parents=True, exist_ok=True)
SR, SECONDS = 48000, 12
N = SR * SECONDS
rng = np.random.default_rng(710173)
bus = np.zeros((N, 2))

def band(x, lo, hi):
    f = np.fft.rfftfreq(len(x), 1 / SR)
    h = (1 - np.exp(-(f / lo)**4)) * np.exp(-(f / hi)**2)
    return np.fft.irfft(np.fft.rfft(x)*h, n=len(x))

def texture(seconds, lo, hi):
    x = band(rng.normal(size=round(seconds*SR)), lo, hi)
    return x / (np.sqrt(np.mean(x*x)) + 1e-12)

def edges(x, attack=.01, release=.08):
    t = np.arange(len(x))/SR
    return x*np.minimum(t/attack, 1)*np.minimum((len(x)-1-np.arange(len(x)))/SR/release, 1)

def add(x, at, gain=1, pan=0):
    start = round(at*SR)
    count = min(len(x), N-start)
    bus[start:start+count] += x[:count,None]*gain*np.sqrt(np.array([1-pan,1+pan])/2)

t = np.arange(N)/SR
build = np.interp(t, [0,.7,2.4,4.5,6.2,7.5,8.1,9.1,12], [0,.07,.12,.19,.29,.36,.16,0,0])
pressure = texture(SECONDS, 23, 102)
pressure *= build*(.75+.15*np.sin(2*np.pi*.61*t)+.1*np.sin(2*np.pi*1.13*t))
add(pressure, 0)
add(texture(SECONDS, 85, 350)*build, 0, .22, -.12)

# Dissonant, almost hidden tonal pressure behind the physical sound.
drone = np.zeros(N)
for f, level in [(47.2,.7),(49.1,.5),(94.4,.24),(100.7,.2),(143.3,.13)]:
    drift = .36*np.sin(2*np.pi*.17*t+rng.uniform(0,6))
    drone += np.sin(2*np.pi*f*t+drift+rng.uniform(0,6))*level
drone *= np.interp(t,[0,1.6,5.7,7.8,9.7,12],[0,.018,.033,.047,.009,0])
add(drone,0,pan=.26)

# Unintelligible breath/whisper layers, kept behind the main action.
for at, duration, pan in [(1.25,2.5,-.72),(3.75,2.4,.69),(6.15,2.2,-.45)]:
    u = np.arange(round(duration*SR))/SR
    syllables = (.5+.3*np.sin(2*np.pi*2.3*u+.8*np.sin(2*np.pi*.7*u))+.2*np.sin(2*np.pi*5.1*u))**2
    air = texture(duration, 650, 4300)*syllables*np.sin(np.pi*np.arange(len(u))/len(u))**2
    add(edges(air,.15,.4), at, .012, pan)

# Soil fractures accelerate with random timing, size, and stereo position.
times = sorted(list(rng.uniform(1.55,4.4,21))+list(rng.uniform(4.4,7.95,107))+list(rng.uniform(8,9.2,20)))
for at in times:
    duration = rng.uniform(.045,.25)
    u = np.arange(round(duration*SR))/SR
    env = (1-np.exp(-u/.001))*np.exp(-u/rng.uniform(.012,.07))
    crack = texture(duration, rng.uniform(280,650), rng.uniform(2300,6500))*env
    level = np.interp(at,[1.5,4.4,7.4,8,9.2],[.02,.055,.12,.055,.007])
    add(edges(crack,.001,.012),at,level*rng.uniform(.5,1.45),rng.uniform(-.8,.8))

# Fibrous roots strain and snap with short, descending resonances.
for at, gain in [(2.7,.04),(3.9,.065),(4.85,.08),(5.6,.10),(6.38,.13),(7.12,.16),(7.67,.10)]:
    duration = rng.uniform(.37,.65)
    u = np.arange(round(duration*SR))/SR
    f = rng.uniform(160,250)
    phase = 2*np.pi*(f*u+120*.06*(1-np.exp(-u/.06)))
    cord = np.sin(phase)+.38*np.sin(phase*2.13)+.18*np.sin(phase*3.37)
    cord = cord*.3+texture(duration,180,2400)*.7
    cord *= (1-np.exp(-u/.003))*np.exp(-u/.095)
    add(edges(cord,.002,.05),at,gain,rng.uniform(-.6,.6))

# Stone slabs grind and shift rather than repeating a regular impact pattern.
for at, duration, gain, pan in [(3.05,1.2,.07,-.35),(4.7,1.6,.11,.45),(6.1,1.8,.15,-.2),(7.35,1.15,.18,.15)]:
    u = np.arange(round(duration*SR))/SR
    friction = texture(duration,90,1700)
    grind = friction*(.55+.30*np.sin(2*np.pi*13.7*u+.7*np.sin(2*np.pi*1.6*u))+.15*np.sin(2*np.pi*32.3*u))
    add(edges(grind*np.sin(np.pi*np.arange(len(u))/len(u))**1.2,.06,.25),at,gain,pan)
for at, gain, freq in [(4.45,.16,53),(5.85,.22,44),(6.93,.29,39),(7.72,.39,34)]:
    u = np.arange(round(.95*SR))/SR
    impact = (.62*texture(.95,28,200)+.38*np.sin(2*np.pi*(freq*u+1.3*(1-np.exp(-u/.055)))))
    impact *= (1-np.exp(-u/.005))**2*np.exp(-u/.21)
    add(edges(impact,.003,.15),at,gain)

# A rough, subharmonic guttural groan swells as the creature rises.
duration = 4.9
u = np.arange(round(duration*SR))/SR
f0 = 35+7*np.sin(np.pi*u/duration)+2.4*np.sin(2*np.pi*1.13*u)+1.1*np.sin(2*np.pi*7.2*u)
phase = 2*np.pi*np.cumsum(f0)/SR
voice = np.zeros(len(u))
for k in range(1,30):
    formant = .12+np.exp(-((k*f0-255)/130)**2)+.7*np.exp(-((k*f0-680)/210)**2)
    voice += np.sin(k*phase+rng.uniform(-.2,.2))*formant/k**.72
voice += .5*np.sin(phase*.5+.4*np.sin(2*np.pi*3*u))
voice /= np.sqrt(np.mean(voice*voice))
voice = np.tanh(voice*1.7)*.65+texture(duration,85,1700)*.35
flutter = .65+.23*np.sin(2*np.pi*2.7*u+.7*np.sin(2*np.pi*.8*u))+.12*np.sin(2*np.pi*15.9*u)
env = np.interp(u,[0,.6,1.7,2.9,3.5,4.2,4.9],[0,.12,.4,.85,.6,.22,0])
add(edges(voice*flutter*env,.2,.5),4.05,.53,-.05)

# Long dark cavern response: dense reflections plus distinct diminishing echoes.
dry = bus.copy()
wet_source = np.column_stack([band(dry[:,ch],55,2800) for ch in range(2)])
ir_len = round(4.8*SR)
ir_t = np.arange(ir_len)/SR
fft_len = 1 << (N+ir_len-2).bit_length()
for ch in range(2):
    ir = rng.normal(size=ir_len)*np.exp(-ir_t/0.82)
    ir *= np.minimum(np.maximum(ir_t-.06,0)/.10,1)
    ir = band(ir,70,3100)
    ir /= np.sqrt(np.sum(ir*ir))
    wet = np.fft.irfft(np.fft.rfft(wet_source[:,ch],fft_len)*np.fft.rfft(ir,fft_len),fft_len)[:N]
    bus[:,ch] += wet*.40
for delay, gain in [(.29,.19),(.57,.14),(.91,.10),(1.37,.07),(1.92,.046),(2.61,.029),(3.34,.017)]:
    offset = round(delay*SR)
    bus[offset:] += wet_source[:-offset,::-1]*gain
for ch in range(2):
    bus[:,ch] = band(bus[:,ch],22,11000)
bus *= np.minimum(t/.08,1)[:,None]*np.minimum((SECONDS-1/SR-t)/.85,1)[:,None]
bus *= 10**(-1.2/20)/np.max(np.abs(bus))
assert np.isfinite(bus).all() and np.max(np.abs(bus)) < 1
assert np.max(np.abs(bus[[0,-1]])) < 1e-8
path = OUT/'Monstrous_Earth_Emergence_Cinematic_12s.wav'
dither = (rng.random(bus.shape)-rng.random(bus.shape))/65536
pcm = np.rint(np.clip(bus+dither,-.9999,.9999)*32767).astype('<i2')
with wave.open(str(path),'wb') as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(pcm.tobytes())
with wave.open(str(path),'rb') as w:
    assert w.getnframes() == N and w.getframerate() == SR and w.getnchannels() == 2
print(json.dumps({'file':str(path),'seconds':SECONDS,'peak_dbfs':round(float(20*np.log10(np.max(np.abs(bus)))),2),'clipped_samples':int(np.count_nonzero(np.abs(pcm)==32767))},indent=2))
