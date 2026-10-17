"""Create dry, irregular creature movement from the user's supplied wet foley."""
from pathlib import Path
import sys
import wave
import json
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT/'tmp/audio_tools'))
import miniaudio

SOURCE = Path(r'C:\Users\atiro\Downloads\eating raw meat sound effect (royalty free).mp3')
OUT = ROOT/'Assets/Sound/CreatureFoley/Wet_Flesh_Reference_Edit_Dry_15s.wav'
SR, LENGTH = 48000, 15
rng = np.random.default_rng(170104)
decoded = miniaudio.decode_file(str(SOURCE), output_format=miniaudio.SampleFormat.FLOAT32, nchannels=1, sample_rate=SR)
source = np.array(decoded.samples,dtype=np.float64)
source -= np.mean(source)

def filter_audio(x, low=55, high=8200):
    f = np.fft.rfftfreq(len(x),1/SR)
    w = (1-np.exp(-(f/low)**4))*np.exp(-(f/high)**4)
    return np.fft.irfft(np.fft.rfft(x)*w,n=len(x))

source = filter_audio(source)
# Gentle compression rounds hard bite-like peaks while keeping wet detail.
source = np.tanh(source*2.5)/2.5
mix = np.zeros(SR*LENGTH)

def edge(x, attack=.065, release=.14):
    a = np.minimum(np.arange(len(x))/(SR*attack),1)
    b = np.minimum(np.arange(len(x))[::-1]/(SR*release),1)
    return x*np.sin(a*np.pi/2)**2*np.sin(b*np.pi/2)**2

def sample(start, duration, speed):
    x = source[round(start*SR):round((start+duration)*SR)]
    return np.interp(np.arange(round(len(x)/speed))*speed,np.arange(len(x)),x)

def add(x, at, gain=1):
    i = round(at*SR)
    n = min(len(x),len(mix)-i)
    if n > 0:
        mix[i:i+n] += x[:n]*gain

# Selected source windows do not preserve the original repeating mouth cadence.
gestures = [(.10,2.18,1.16,.66,1.12),
            (2.17,5.35,1.28,.72,1.06),
            (4.28,8.42,1.30,.64,1.22),
            (6.70,3.78,1.32,.69,1.04),
            (8.93,10.48,1.22,.67,1.04),
            (11.02,6.67,1.14,.61,1.12),
            (13.15,13.37,.97,.68,1.65)]
for at, start, duration, speed, gain in gestures:
    body = sample(start,duration,speed)
    u = np.arange(len(body))/SR
    modulation = .82+.12*np.sin(2*np.pi*.62*u+rng.uniform(0,6))+.06*np.sin(2*np.pi*1.31*u)
    add(edge(body*modulation),at,gain)

# Small source grains form sustained sticky pulls, detached from chewing timing.
for at,duration,start,gain in [(1.10,1.03,9.12,.30),(3.18,.86,4.72,.32),
                              (5.34,1.11,6.2,.34),(7.60,.96,2.94,.31),
                              (9.83,.90,11.31,.32),(12.13,.94,5.75,.30)]:
    stretch = np.zeros(round((duration+.3)*SR))
    for onset in np.arange(0,duration,.055):
        position = start+onset*.23+rng.uniform(-.035,.035)
        grain = sample(position,rng.uniform(.075,.13),rng.uniform(.52,.70))
        # Reverse a minority of grains to soften recognizable attack patterns.
        if rng.random()<.27:
            grain = grain[::-1]
        grain = edge(grain,.035,.055)
        i = round(onset*SR)
        n = min(len(grain),len(stretch)-i)
        stretch[i:i+n] += grain[:n]
    stretch = filter_audio(stretch,95,4700)
    add(edge(stretch,.10,.18),at,gain)

# Individual wet detail accents at deliberately unequal intervals.
for at,start,speed,gain in [(2.03,3.16,.85,.33),(4.12,6.03,.78,.28),
                            (6.46,10.91,.88,.35),(8.72,2.68,.82,.27),
                            (10.88,7.84,.76,.32),(14.42,5.84,.81,.28)]:
    add(edge(sample(start,.13,speed),.012,.055),at,gain)

mix = filter_audio(mix,48,8800)
mix = np.tanh(mix*2)/2
t = np.arange(len(mix))/SR
mix *= np.minimum(t/.045,1)*np.minimum((LENGTH-1/SR-t)/.20,1)
mix *= 10**(-1.5/20)/np.max(np.abs(mix))
assert np.isfinite(mix).all() and abs(mix[0])+abs(mix[-1])<1e-9
dither = (rng.random(len(mix))-rng.random(len(mix)))/65536
pcm = np.rint(np.clip(mix+dither,-.9999,.9999)*32767).astype('<i2')
OUT.parent.mkdir(parents=True,exist_ok=True)
with wave.open(str(OUT),'wb') as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(pcm.tobytes())
with wave.open(str(OUT),'rb') as w:
    assert w.getnframes()/w.getframerate()==LENGTH
print(json.dumps({'output':str(OUT),'seconds':LENGTH,'sample_rate':SR,'channels':1,'peak_dbfs':round(float(20*np.log10(np.max(np.abs(mix)))),2),'clipped_samples':int(np.count_nonzero(np.abs(pcm)==32767))},indent=2))
