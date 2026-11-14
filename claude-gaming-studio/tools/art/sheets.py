import sys
from PIL import Image, ImageDraw, ImageFont
from render import render
import world as w
def sheet(cells, path, cw=420, ch=460, cols=4, SS=2):
    rows = (len(cells) + cols - 1) // cols
    im = Image.new('RGB', (cw * cols, ch * rows), (232, 226, 214)); d = ImageDraw.Draw(im)
    f = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf', 22)
    for i, (fn, label, yaw, pit) in enumerate(cells):
        t = render(fn(), cw * SS, ch * SS, yaw, pit).resize((cw, ch), Image.LANCZOS)
        im.paste(t, ((i % cols) * cw, (i // cols) * ch)); d.text(((i % cols) * cw + 16, (i // cols) * ch + 12), label, fill=(40, 40, 46), font=f)
    im.save(path)
which = sys.argv[1] if __name__ == "__main__" else ""
if which in ('char', 'all'):
    sheet([(w.character, 'Front', -25, 8), (w.character, 'Side', -80, 8), (w.character, 'Back', 155, 8), (w.char_helmet, 'Helmet', -30, 10)], 'sheet_char.png', 440, 620)
if which in ('gear', 'all'):
    sheet([(w.crate, 'Crate', -30, 20), (w.barrel, 'Barrel 100 L', -30, 18), (w.drum, 'Drum 200 L', -30, 18), (w.ibc, 'IBC tank 1,000 L', -30, 18),
           (w.pump, 'Pump', -30, 20), (w.motorkit, 'Motor kit', -30, 22), (w.inlet, 'Inlet fitting', -30, 30), (w.tower, 'Launch tower', -30, 8)], 'sheet_gear.png')
if which in ('world', 'all'):
    sheet([(w.tree, 'Tree', -30, 10), (w.bush, 'Bush', -30, 20), (w.house_a, 'Cottage', -30, 18), (w.house_d, 'Bungalow', -30, 18),
           (w.house_b, 'Shop house', -30, 14), (w.house_c, 'Townhouse', -30, 14), (w.garage, 'Garage', -30, 18), (w.truck_red, 'Delivery lorry', -40, 16),
           (w.truck_yellow, 'Water tanker', -40, 16), (w.fountain, 'Fountain', -30, 24), (w.statue, 'Company statue', -30, 12), (w.flag, 'Flag', -60, 8)], 'sheet_world.png')
