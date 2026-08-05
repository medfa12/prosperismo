# Renders the AMBIENT BACKGROUND MODEL MEASURED FROM
# ps5oracle/shell_ui/live_background/default.mp4.
# Every constant below is a measurement from that clip (see
# docs/sony-shell/reference-video-grading.md), NOT a value recovered from
# shader ISA. Provenance of the clip itself is NOT established.
import numpy as np
from PIL import Image
from scipy import ndimage

W,H=1920,1080
rng=np.random.default_rng(7)
yy,xx=np.mgrid[0:H,0:W].astype(np.float32)

# --- 1. basemat (measured: darkest smoothed field of the 43-frame mean) ---
img=np.zeros((H,W,3),np.float32)
img[:]= np.array([4.93,9.80,21.92],np.float32)

# --- 2. light shaft: apex, asymmetric-gaussian angular profile, d^-1.4 ---
Ax,Ay=296.1,-463.4
A,mu,sl,sr = 87.4, 1.30, 16.06, 28.70
d=np.hypot(xx-Ax,yy-Ay); th=np.degrees(np.arctan2(xx-Ax,yy-Ay))
sig=np.where(th<mu,sl,sr)
shaft=A*np.exp(-0.5*((th-mu)/sig)**2)*(d/600.0)**-1.4
shaft=np.clip(shaft,0,None)
img+=shaft[...,None]*np.array([1.24,1.00,0.84],np.float32)   # measured colour ratio

# --- 3. bottom-left glow band (measured centre, widths, colour ratio) ---
band=74.0*np.exp(-0.5*((yy-940.0)/110.0)**2)*np.exp(-np.clip(xx-400.0,0,None)/700.0)
band*=np.clip(0.6+0.4*xx/400.0,0,1)
img+=band[...,None]*np.array([1.00,1.00,0.85],np.float32)

# --- 4. particles: 533/frame, measured radius CDF, measured density map ---
# measured vertical / horizontal marginals (counts per frame)
vm=np.array([46.8,23.5,8.9,21.6,81.5,144.1,110.0,55.8,33.0,7.6]); vm/=vm.sum()
hm=np.array([24.4,81.1,63.8,55.3,45.4,38.3,47.8,76.8,54.0,46.0]); hm/=hm.sum()
N=533
by=rng.choice(10,N,p=vm); bx=rng.choice(10,N,p=hm)
py=(by+rng.random(N))*108.0; px=(bx+rng.random(N))*192.0
# measured FWHM-radius percentiles -> inverse-CDF sample
q =np.array([0,10,25,50,75,90,99,100])/100.0
rv=np.array([0.9,1.13,1.26,2.03,4.41,7.23,13.54,31.17])
rad=np.interp(rng.random(N),q,rv)
# measured peak-increment percentiles
qp=np.array([0,10,50,90,99,100])/100.0
pv=np.array([4.0,7.2,26.6,82.6,210.2,237.7])
peak=np.interp(rng.random(N),qp,pv)
for i in range(N):
    R=rad[i]; ext=int(np.ceil(R*1.25))+2
    x0,y0=int(px[i]),int(py[i])
    a0,a1=max(0,y0-ext),min(H,y0+ext); b0,b1=max(0,x0-ext),min(W,x0+ext)
    if a1<=a0 or b1<=b0: continue
    sy,sx=np.mgrid[a0:a1,b0:b1]
    rr=np.hypot(sy-py[i],sx-px[i])/R
    # measured radial profile: flat to 0.6R, smooth roll-off to zero at 1.2R
    t=np.clip((1.2-rr)/0.6,0,1); prof=t*t*(3-2*t)
    # measured colour: saturation rises with radius, hue ~34 deg
    s=np.clip(0.20+0.055*R,0.13,0.65)
    ratio=np.array([1.0+0.46*s, 1.0, 1.0-0.78*s],np.float32)
    img[a0:a1,b0:b1]+=(prof*peak[i])[...,None]*ratio
# slight defocus of the whole particle layer edge (codec/optics), measured softness
img=np.dstack([ndimage.gaussian_filter(img[:,:,i],0.6) for i in range(3)])

out=np.clip(img,0,255).astype(np.uint8)
Image.fromarray(out).save('reference_matched_t0.png')
l=0.2126*out[:,:,0]+0.7152*out[:,:,1]+0.0722*out[:,:,2]
print("wrote reference_matched_t0.png")
print("render  : luma mean %.2f  p1 %.2f  p99 %.2f  pct>128 %.3f"%(l.mean(),np.percentile(l,1),np.percentile(l,99),(l>128).mean()*100))
print("reference: luma mean 38.44  p1 9.36  p99 104.27  pct>128 0.272")
