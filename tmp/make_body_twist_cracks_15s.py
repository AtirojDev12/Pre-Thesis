"""Dry body contortions: strain, joint cracks, fractures, and low tissue mass."""
from pathlib import Path
import wave
import json
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'Assets/Sound/CreatureFoley/Body_Twist_Bone_Cracks_Flesh_Dry_15s.wav'
SR, SECONDS = 48000, 15
rng = np.random.default_rng(171026)
mix = np.zeros(SR*SECONDS)

def band(x, lo, hi):
    f = np.fft.rfftfreq(len(x),1/SR)
    h = (1-np.exp(-(f/lo)**4))*np.exp(-(f/hi)**4)
    return np.fft.irfft(np.fft.rfft(x)*h,n=len(x))

def noise(length, lo, hi):
    a = band(rng.normal(size=round(length*SR)),lo,hi)
    return a/(np.sqrt(np.mean(a*a))+1e-12)

def fade(a, attack=.005, release=.025):
    a = a.copy()
    a *= np.minimum(np.arange(len(a))/(SR*attack),1)
    a *= np.minimum(np.arange(len(a))[::-1]/(SR*release),1)
    return a

def add(a, at, gain=1):
    i = round(at*SR)
    n = min(len(a),len(mix)-i)
    if n>0:
        mix[i:i+n] += a[:n]*gain

with wave.open(str(ROOT/'Assets/Sound/CreatureFoley/Wet_Flesh_Reference_Edit_Dry_15s.wav'),'rb') as w:
    flesh = np.frombuffer(w.readframes(w.getnframes()),'<i2').astype(float)/32768
flesh = band(flesh,80,680)
flesh /= np.sqrt(np.mean(flesh*flesh))+1e-12

def flesh_pull(duration, seed_position):
    # Reassemble tiny overlapping low-pass grains, removing mouth/bite sequences.
    a = np.zeros(round((duration+.16)*SR))
    for offset in np.arange(0,duration,.047):
        start = round((seed_position+rng.uniform(0,.65))*SR)
        g = flesh[start:start+round(.085*SR)]
        speed = rng.uniform(.58,.77)
        g = np.interp(np.arange(round(len(g)/speed))*speed,np.arange(len(g)),g)
        if rng.random()<.5:
            g = g[::-1]
        g = fade(g,.025,.045)
        i = round(offset*SR)
        n = min(len(g),len(a)-i)
        a[i:i+n] += g[:n]
    a /= np.sqrt(np.mean(a*a))+1e-12
    return fade(a,.1,.16)

def fracture(weight):
    length = rng.uniform(.15,.24)
    t = np.arange(round(length*SR))/SR
    # Sharp brittle failure with a thick, very short body underneath.
    splinter = noise(length,430,7200)*np.exp(-t/.014)
    crunch = noise(length,160,3300)*np.exp(-t/.047)
    thock = noise(length,80,570)*np.exp(-t/.037)
    crack = .44*splinter+.34*crunch+.22*thock
    return fade(crack,.00022,.025)*weight

# Each action has its own build-up, irregular failure cluster, and tissue recoil.
actions = [(.18,1.48,.32,1.7), (2.38,1.18,.40,4.7),
           (4.40,1.69,.35,7.1), (6.95,1.24,.50,9.8),
           (9.15,1.68,.39,2.8), (11.75,1.15,.46,11.8),
           (13.50,.85,.30,6.0)]
for index,(at,duration,weight,source_pos) in enumerate(actions):
    n = round(duration*SR)
    t = np.arange(n)/SR
    # Friction creaks, not a pitched groan: irregular stick-slip under tension.
    jitter = np.interp(t,np.linspace(0,duration,35),rng.uniform(.25,1,35))
    phase = 2*np.pi*np.cumsum(28+51*t/duration+9*np.sin(2*np.pi*2.3*t))/SR
    slip = np.maximum(np.sin(phase),0)**12
    tension = noise(duration,170,2300)*(.15+.85*slip)*jitter
    env = np.sin(np.pi*np.arange(n)/n)**.9
    add(fade(tension*env,.045,.055),at,.085)
    add(flesh_pull(duration*.92,source_pos),at+.04,.027)
    failure = at+duration*.69
    add(fracture(weight),failure)
    # Cluster shape varies; no regular eating/chewing beat.
    for k in range(rng.integers(3,7)):
        delta = rng.uniform(.018,.22)
        add(fracture(weight*rng.uniform(.14,.40)),failure+delta)
    # Lower knuckle-like ticks occur during the twist, before the main break.
    for offset in rng.uniform(.16,duration*.61,rng.integers(2,5)):
        add(fracture(weight*.16),at+offset)
    length = .37
    u = np.arange(round(length*SR))/SR
    recoil = noise(length,55,370)*(1-np.exp(-u/.007))*np.exp(-u/.085)
    add(fade(recoil,.004,.05),failure+.025,weight*.25)
    add(flesh_pull(.36,source_pos+.3),failure+.06,.038)

mix = band(mix,42,10500)
mix = np.tanh(mix*1.1)/1.1
t = np.arange(len(mix))/SR
mix *= np.minimum(t/.025,1)*np.minimum((SECONDS-1/SR-t)/.22,1)
mix *= 10**(-1.5/20)/np.max(np.abs(mix))
assert np.isfinite(mix).all() and np.max(np.abs(mix))<1
assert abs(mix[0])+abs(mix[-1])<1e-10
dither = (rng.random(len(mix))-rng.random(len(mix)))/65536
pcm = np.rint(np.clip(mix+dither,-.9999,.9999)*32767).astype('<i2')
with wave.open(str(OUT),'wb') as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(pcm.tobytes())
with wave.open(str(OUT),'rb') as w:
    assert w.getnframes()/w.getframerate()==SECONDS
print(json.dumps({'file':str(OUT),'seconds':SECONDS,'sample_rate':SR,'channels':1,'peak_dbfs':round(float(20*np.log10(np.max(np.abs(mix)))),2),'clipped_samples':int(np.count_nonzero(np.abs(pcm)==32767))},indent=2))
