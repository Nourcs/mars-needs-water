# The real city cores the game rebuilds (bake_city.py). One entry per city:
#   box    lon0, lat0, lon1, lat1 of the real core
#   site   where it stands in the game (lat, lon); left out = its true place
#   sea    True if the core touches the sea (its water is then the game's sea level)
#   apron  extra open water added beyond the box edge, game metres: w, e, s, n
#   land   'osm' = real land and water outlines; 'built' = wherever there are streets or buildings
#   blocks True = mid-rise buildings are merged into city blocks; False = houses along the streets instead
#   tall   real height (m) from which a building is rebuilt on its own footprint
#   floors floors to assume where the map has no height
#   pal    colour family (PALS below)
#   hills  (lat, lon, flat radius, outer radius, height) in game metres above the city's ground
#   named  building name -> shape flag (1 pyramid, 2 tapering crown)
PALS = {
    'us':     dict(mid=[(222, 205, 176), (196, 170, 140), (180, 110, 90), (214, 214, 206), (168, 160, 150), (230, 220, 200), (150, 120, 100)],
                   lots=[(255, 237, 204), (219, 237, 255), (255, 217, 209), (219, 247, 224), (255, 247, 219), (237, 224, 255), (250, 230, 194), (255, 255, 255)]),
    'paris':  dict(mid=[(232, 222, 200), (224, 212, 188), (238, 230, 212), (214, 204, 184), (226, 216, 196)],
                   lots=[(250, 240, 220), (244, 232, 208), (255, 248, 232), (236, 226, 206)]),
    'london': dict(mid=[(196, 170, 140), (180, 120, 95), (222, 214, 200), (160, 140, 125), (205, 195, 180), (230, 224, 212)],
                   lots=[(230, 200, 170), (214, 150, 124), (245, 238, 226), (200, 176, 156), (255, 250, 240)]),
    'med':    dict(mid=[(232, 196, 150), (222, 170, 120), (240, 214, 170), (214, 150, 110), (236, 224, 200), (226, 184, 140)],
                   lots=[(250, 214, 168), (240, 188, 138), (255, 232, 188), (232, 168, 128), (252, 240, 216)]),
    'asia':   dict(mid=[(214, 214, 210), (190, 196, 200), (228, 224, 216), (170, 176, 184), (204, 198, 188)],
                   lots=[(240, 240, 236), (220, 226, 230), (250, 246, 238), (206, 212, 220), (236, 228, 214)]),
    'desert': dict(mid=[(226, 206, 170), (214, 190, 150), (238, 226, 200), (204, 180, 146)],
                   lots=[(246, 226, 190), (236, 212, 172), (255, 244, 220), (226, 200, 164)]),
    'east':   dict(mid=[(226, 210, 180), (200, 180, 160), (214, 196, 170), (180, 150, 140), (236, 226, 210), (196, 206, 196)],
                   lots=[(246, 230, 200), (224, 204, 184), (255, 246, 230), (214, 226, 214), (240, 214, 200)]),
}
CITIES = {
    'sanfrancisco': dict(box=(-122.4225, 37.7745, -122.3865, 37.8115), site=(38.1, -121.35), sea=True, apron=(0, 170, 0, 330), land='built', blocks=False, tall=20, pal='us',
                         named={'Transamerica Pyramid': 1, 'Salesforce Tower': 2, '181 Fremont Tower': 2, 'Millennium Tower': 2}, motorway_east=150,
                         # The bluffs that carry the Golden Gate Bridge (the landmark stands off the north-west tip: its deck
                         # ends are at (-231, 384) and (-229, 664), 15.3 m above the streets), and the road off each end.
                         hills_xz=[(-231, 362, 24, 96, 15.3), (-229, 696, 34, 150, 15.3), (-215, 800, 0, 120, 41)],
                         extra=[[(-231, 386), (-231, 340), (-231, 300), 'join'], [(-229, 662), (-229, 700), (-222, 740), (-205, 775)]]),
    'paris':        dict(box=(2.285, 48.845, 2.355, 48.878), pal='paris', floors=6),
    'london':       dict(box=(-0.150, 51.492, -0.070, 51.522), pal='london', floors=5),
    'newyork':      dict(box=(-74.008, 40.735, -73.962, 40.772), pal='us', floors=6, sea=True),
    'tokyo':        dict(box=(139.728, 35.650, 139.778, 35.690), pal='asia', floors=5),
    'rome':         dict(box=(12.447, 41.882, 12.503, 41.912), pal='med', floors=5),
    'sydney':       dict(box=(151.190, -33.885, 151.232, -33.845), pal='us', floors=4, sea=True),
    'rio':          dict(box=(-43.215, -22.975, -43.160, -22.938), pal='med', floors=5, sea=True,
                         hills=[(-22.9519, -43.2105, 14, 150, 62), (-22.9492, -43.1563, 8, 70, 36)]),
    'dubai':        dict(box=(55.255, 25.180, 55.295, 25.215), pal='desert', floors=5),
    'agra':         dict(box=(78.020, 27.160, 78.060, 27.190), pal='desert', floors=2),
    'barcelona':    dict(box=(2.150, 41.375, 2.195, 41.410), pal='med', floors=6, sea=True),
    'moscow':       dict(box=(37.595, 55.738, 37.650, 55.768), pal='east', floors=6),
    'berlin':       dict(box=(13.360, 52.503, 13.420, 52.530), pal='east', floors=5),
    'athens':       dict(box=(23.712, 37.960, 23.748, 37.990), pal='med', floors=5, hills=[(37.9715, 23.7267, 30, 85, 14)]),
    'toronto':      dict(box=(-79.405, 43.635, -79.360, 43.665), pal='us', floors=4),
    'seattle':      dict(box=(-122.365, 47.598, -122.320, 47.630), pal='us', floors=4, sea=True),
    'pisa':         dict(box=(10.385, 43.705, 10.415, 43.728), pal='med', floors=4),
    'losangeles':   dict(box=(-118.272, 34.035, -118.235, 34.065), pal='us', floors=3),
    'hongkong':     dict(box=(114.148, 22.272, 114.188, 22.305), pal='asia', floors=12, sea=True),
    'singapore':    dict(box=(103.838, 1.272, 103.872, 1.302), pal='asia', floors=6, sea=True),
    'shanghai':     dict(box=(121.470, 31.222, 121.515, 31.252), pal='asia', floors=6),
    'chicago':      dict(box=(-87.650, 41.868, -87.608, 41.900), pal='us', floors=5),
    'istanbul':     dict(box=(28.950, 40.998, 28.995, 41.030), pal='med', floors=5, sea=True),
    'venice':       dict(box=(12.312, 45.425, 12.356, 45.448), pal='med', floors=4, sea=True),
}
# The wonders (real places) and how much room each model needs, game metres (a radius, plus extra circles at
# offsets east and north for the long ones). Buildings are kept clear of them.
LANDMARKS = [('eiffel', 48.8584, 2.2945, 18), ('liberty', 40.6892, -74.0445, 26), ('colosseum', 41.8902, 12.4922, 32), ('bigben', 51.5007, -0.1246, 22, [(0, -30, 24), (0, -62, 26)]),
             ('operahouse', -33.8568, 151.2153, 42), ('redeemer', -22.9519, -43.2105, 16), ('burj', 25.1972, 55.2744, 30), ('pisa', 43.7230, 10.3966, 24, [(-30, 0, 30), (-58, 0, 24)]),
             ('tajmahal', 27.1751, 78.0421, 50), ('spaceneedle', 47.6205, -122.3493, 18), ('cntower', 43.6426, -79.3871, 18), ('stbasil', 55.7525, 37.6231, 26),
             ('sagrada', 41.4036, 2.1744, 40), ('tokyotower', 35.6586, 139.7454, 16), ('parthenon', 37.9715, 23.7267, 30), ('brandenburg', 52.5163, 13.3777, 36)]
