"""Dry procedural foley: irregular sticky pulls, fluid, and fibrous tearing."""
from pathlib import Path
import wave
import json
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/Sound/CreatureFoley'
OUT.mkdir(parents=True, exist_ok=True)
SR, SECONDS = 48000, 15
rng = np.random.default_rng(2017063)
audio = np.zeros(SR*SECONDS)

def band(x, low, high):
    freq = np.fft.rfftfreq(len(x), 1/SR)
    gain = (1-np.exp(-(freq/low)**4))*np.exp(-(freq/high)**2)
    return np.fft.irfft(np.fft.rfft(x)*gain, n=len(x))

def noise(duration, low, high):
    x = band(rng.normal(size=round(duration*SR)), low, high)
    return x/(np.sqrt(np.mean(x*x))+1e-12)

def soften(x, attack=.008, release=.035):
    t = np.arange(len(x))/SR
    return x*np.minimum(t/attack,1)*np.minimum((len(x)-1-np.arange(len(x)))/SR/release,1)

def add(x, at, gain=1):
    start = round(at*SR)
    count = min(len(x),len(audio)-start)
    if count > 0:
        audio[start:start+count] += x[:count]*gain

def glob(duration, freq, decay):
    t = np.arange(round(duration*SR))/SR
    # Brief liquid cavity collapse with a descending, noisy, inharmonic body.
    phase = 2*np.pi*(freq*t+freq*.7*.019*(1-np.exp(-t/.019)))
    body = np.sin(phase)+.25*np.sin(phase*1.71+.5)
    wet = noise(duration,110,2200)
    env = (1-np.exp(-t/.0025))**2*np.exp(-t/decay)
    return soften((body*.5+wet*.5)*env,.002,.035)

# Uneven slow motions, with small spaces between the broad pressure changes.
motions = [(.12,2.05,.15), (2.35,1.60,.19), (4.18,2.40,.17),
           (6.91,1.85,.22), (8.92,2.23,.18), (11.43,2.05,.20), (13.55,1.12,.13)]
for index,(at,duration,level) in enumerate(motions):
    t = np.arange(round(duration*SR))/SR
    envelope = np.sin(np.pi*np.arange(len(t))/len(t))**rng.uniform(1.3,2.1)
    # Broadband flesh compression and a wet surface moving against itself.
    slow = np.interp(t,np.linspace(0,duration,11),rng.uniform(.18,1,11))
    slow = np.convolve(slow,np.ones(241)/241,mode='same')
    bulk = noise(duration,45,410)*.56 + noise(duration,220,1800)*.44
    surface = noise(duration,650,4400)
    flutter = .58+.26*np.sin(2*np.pi*rng.uniform(8,15)*t+.6*np.sin(2*np.pi*.8*t))
    movement = (bulk*slow+surface*.16*flutter)*envelope
    add(soften(movement,.035,.10),at,level)
    # Sticky filaments part irregularly within each pull; no repeated rhythm.
    for offset in sorted(rng.uniform(.15,duration-.10,rng.integers(12,23))):
        length = rng.uniform(.023,.115)
        u = np.arange(round(length*SR))/SR
        tear = noise(length,rng.uniform(330,700),rng.uniform(2100,4300))
        tear *= (1-np.exp(-u/.0015))*np.exp(-u/rng.uniform(.008,.032))
        add(soften(tear,.001,.01),at+offset,rng.uniform(.018,.055))
    # Soft suction releases and heavier fluid pockets, never mouth-like cycles.
    for offset in rng.uniform(.22,duration-.12,rng.integers(3,6)):
        add(glob(.24,rng.uniform(95,235),rng.uniform(.025,.065)),at+offset,rng.uniform(.035,.09))

# Longer tendon-like stretching: friction pulses and short creaks under strain.
for at,duration,level in [(1.05,1.08,.07),(4.9,1.35,.09),(7.38,.93,.10),(10.02,1.12,.075),(12.32,.87,.08)]:
    t = np.arange(round(duration*SR))/SR
    phase = 2*np.pi*np.cumsum(64+31*t/duration+6*np.sin(2*np.pi*1.7*t))/SR
    friction = np.maximum(np.sin(phase),0)**9
    grain = noise(duration,190,3000)
    tension = (.18+friction)*grain*np.sin(np.pi*np.arange(len(t))/len(t))**1.7
    add(soften(tension,.025,.065),at,level)

# Thick drips land close to the microphone, without room reflections.
for at,level in [(2.18,.07),(3.98,.095),(6.66,.065),(8.76,.09),(11.24,.085),(13.64,.06),(14.32,.08)]:
    add(glob(.32,rng.uniform(140,280),.048),at,level)
    length = .17
    t = np.arange(round(length*SR))/SR
    splash = noise(length,850,5200)*(1-np.exp(-t/.001))*np.exp(-t/.021)
    add(soften(splash,.001,.025),at+.006,level*.27)

# Remove inaudible offset and soften file boundaries; no spatial processing.
audio = band(audio,32,10500)
t = np.arange(len(audio))/SR
audio *= np.minimum(t/.025,1)*np.minimum((SECONDS-1/SR-t)/.24,1)
audio *= 10**(-1.5/20)/np.max(np.abs(audio))
assert np.isfinite(audio).all() and np.max(np.abs(audio)) < 1
assert abs(audio[0])+abs(audio[-1]) < 1e-9
path = OUT/'Wet_Visceral_Flesh_Writhing_Dry_15s.wav'
dither = (rng.random(len(audio))-rng.random(len(audio)))/65536
pcm = np.rint(np.clip(audio+dither,-.9999,.9999)*32767).astype('<i2')
with wave.open(str(path),'wb') as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(pcm.tobytes())
with wave.open(str(path),'rb') as w:
    assert w.getnframes()/w.getframerate() == SECONDS and w.getnchannels() == 1
print(json.dumps({'file':str(path),'seconds':SECONDS,'channels':1,'sample_rate':SR,'peak_dbfs':round(float(20*np.log10(np.max(np.abs(audio)))),2),'clipped_samples':int(np.count_nonzero(np.abs(pcm)==32767))},indent=2))
