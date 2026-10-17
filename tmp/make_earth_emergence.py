from pathlib import Path
import json
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/Sound/GhostEmergence'
OUT.mkdir(parents=True, exist_ok=True)
SR = 48000
DURATION = 16.0
rng = np.random.default_rng(814703)
mix = np.zeros((int(SR*DURATION), 2))

def noise(seconds, low, high):
    n = round(seconds*SR)
    freq = np.fft.rfftfreq(n, 1/SR)
    weights = (1-np.exp(-(freq/low)**4))*np.exp(-(freq/high)**2)
    y = np.fft.irfft(np.fft.rfft(rng.normal(size=n))*weights, n=n)
    return y/(np.sqrt(np.mean(y*y))+1e-12)

def add(y, at, level=1, pan=0):
    start = round(at*SR)
    count = min(len(y), len(mix)-start)
    if count <= 0:
        return
    mix[start:start+count,0] += y[:count]*level*np.sqrt((1-pan)/2)
    mix[start:start+count,1] += y[:count]*level*np.sqrt((1+pan)/2)

def taper(y, attack=.01, release=.08):
    t = np.arange(len(y))/SR
    return y*np.minimum(t/attack,1)*np.minimum((len(y)-1-np.arange(len(y)))/SR/release,1)

# Underground pressure grows unevenly, then falls away beneath the emergence.
t = np.arange(len(mix))/SR
shape = np.interp(t, [0,1.2,3.4,5.5,7.0,8.6,10.3,12.4,14.3,16], [0,.045,.11,.19,.28,.32,.22,.12,.055,0])
rumble = noise(DURATION,25,145)
wobble = .64+.20*np.sin(2*np.pi*.43*t)+.12*np.sin(2*np.pi*.79*t+1.6)
add(rumble*shape*wobble,0)
add(noise(DURATION,95,480)*shape*.17,0,pan=.22)

# Sparse early fissures become clusters of snapping soil and falling fragments.
events = sorted(list(rng.uniform(1.8,5.6,22))+list(rng.uniform(5.6,10.7,115))+list(rng.uniform(10.7,14.5,42)))
for at in events:
    length = rng.uniform(.035,.24)
    u = np.arange(round(length*SR))/SR
    env = (1-np.exp(-u/.0012))*np.exp(-u/rng.uniform(.008,.055))
    crack = noise(length,rng.uniform(240,700),rng.uniform(1800,6000))*env
    strength = np.interp(at,[1.8,5.6,8.7,11,14.5],[.025,.07,.16,.06,.007])
    add(taper(crack,.001,.015),at,strength*rng.uniform(.5,1.6),rng.uniform(-.72,.72))

# Massive slabs shift, with dull body impacts underneath their brittle edges.
for at, level, bass in [(3.7,.13,55),(5.35,.21,46),(6.65,.27,42),(7.73,.37,37),(8.52,.30,44),(9.65,.19,50),(11.15,.12,48)]:
    u = np.arange(round(1.25*SR))/SR
    phase = 2*np.pi*(bass*u+15*.055*(1-np.exp(-u/.055)))
    impact = (noise(1.25,32,230)*.58+np.sin(phase)*.42)*(1-np.exp(-u/.006))**2*np.exp(-u/.23)
    add(taper(impact,.003,.15),at,level,rng.uniform(-.2,.2))
    tear = noise(.68,160,2400)
    v = np.arange(len(tear))/SR
    tear *= (1-np.exp(-v/.025))*np.exp(-v/.16)*(.55+.45*np.sin(2*np.pi*19*v)**2)
    add(taper(tear),at+.025,level*.32,rng.uniform(-.55,.55))

# Strained friction: an uneven, nonverbal creature texture pushing through soil.
length = 6.4
u = np.arange(round(length*SR))/SR
f0 = 39+4.8*np.sin(2*np.pi*.37*u)+2.3*np.sin(2*np.pi*1.21*u)
phase = 2*np.pi*np.cumsum(f0)/SR
voice = np.zeros(len(u))
for harmonic in range(1,19):
    # Broad low formants make the sound coarse and bodily, without speech.
    freq = harmonic*f0
    formant = .13+np.exp(-((freq-270)/140)**2)+.6*np.exp(-((freq-710)/230)**2)
    voice += np.sin(harmonic*phase+rng.uniform(-.25,.25))*formant/(harmonic**.72)
voice /= np.sqrt(np.mean(voice*voice))
rough = noise(length,75,1300)
pulse = (.55+.30*np.sin(2*np.pi*2.17*u+.8*np.sin(2*np.pi*.41*u))+.15*np.sin(2*np.pi*7.8*u))
env = np.interp(u,[0,.8,1.9,2.3,3.3,4.2,5.1,6.4],[0,.16,.43,.27,.65,.42,.24,0])
strain = (voice*.42+rough*.58)*env*pulse
add(taper(strain,.15,.6),5.7,.38,-.08)

# Dirt pours off the emerging body in successive uneven waves.
for at, length, level, pan in [(6.15,1.7,.045,-.5),(7.9,2.6,.10,.45),(9.6,2.1,.072,-.3),(11.1,1.8,.039,.28),(12.8,1.2,.018,-.2)]:
    u = np.arange(round(length*SR))/SR
    flow = noise(length,320,3400)
    env = np.sin(np.pi*np.arange(len(u))/len(u))**1.4
    flow *= env*(.55+.3*np.sin(2*np.pi*5.6*u)+.15*np.sin(2*np.pi*13.3*u))
    add(taper(flow),at,level,pan)

# Short, dark reflections link the layers; the final tail fully decays.
dry = mix.copy()
for delay, level in [(.061,.12),(.109,.085),(.173,.06),(.263,.037),(.391,.022),(.57,.012)]:
    offset = round(delay*SR)
    mix[offset:] += dry[:-offset,::-1]*level
freq = np.fft.rfftfreq(len(mix),1/SR)
weight = (1-np.exp(-(freq/24)**6))*np.exp(-(freq/10500)**4)
for ch in range(2):
    mix[:,ch] = np.fft.irfft(np.fft.rfft(mix[:,ch])*weight,n=len(mix))
mix *= np.minimum(t/.12,1)[:,None]*np.minimum((DURATION-1/SR-t)/1.3,1)[:,None]
mix *= 10**(-1.5/20)/np.max(np.abs(mix))
assert np.isfinite(mix).all() and np.max(np.abs(mix)) < 1
assert np.max(np.abs(mix[[0,-1]])) < 1e-8
path = OUT/'Creature_Emerging_From_Earth_16s.wav'
dither = (rng.random(mix.shape)-rng.random(mix.shape))/65536
pcm = np.rint(np.clip(mix+dither,-.9999,.9999)*32767).astype('<i2')
with wave.open(str(path),'wb') as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(pcm.tobytes())
with wave.open(str(path),'rb') as w:
    assert w.getnframes()/w.getframerate() == DURATION
    assert w.getnchannels() == 2
print(json.dumps({'file':str(path),'duration_seconds':DURATION,'sample_rate':SR,'channels':2,'peak_dbfs':round(float(20*np.log10(np.max(np.abs(mix)))),2),'clipped_samples':int(np.count_nonzero(np.abs(pcm)==32767))},indent=2))
