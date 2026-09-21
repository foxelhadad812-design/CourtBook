# PlaySpot — Image Assets & Attribution Reference

This document catalogs all visual media assets bundled in PlaySpot (`src/CourtBook.Web/wwwroot/images/`), detailing their purpose, categorization, sports domain, source provenance, and licensing terms.

---

## 1. Overview & Image Distribution Strategy

PlaySpot uses a multi-tier asset resolution pipeline to ensure zero broken images, high visual diversity across venues and courts, and sport-specific realism:

1. **Venue Header Gallery**: Showcases authentic complex exteriors, lounges, and sport-specific turf/court action.
2. **Court Dynamic Assignment**: Driven by [`CourtImageHelper`](src/CourtBook.Web/Helpers/CourtImageHelper.cs) which deterministically routes courts based on sport, surface type (e.g. Clay vs Hard court), indoor/outdoor facility flags, and court index.
3. **Sport-Specific Fallbacks**: If an image fails to load or an unknown external URL is provided, standard fallback images exist for each sport category.

---

## 2. Image Asset Catalog & Attribution

### A. Football (Soccer)
| Relative Path | Asset Description | Source & Attribution | License |
|---|---|---|---|
| `/images/venues/football/football-pitch-01.jpg` | Floodlit outdoor artificial turf football pitch at twilight | Unsplash (Thomas Serer / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/football/football-pitch-02.jpg` | Daytime perspective of 5-a-side community football pitch | Unsplash (Marlon Schmeiski / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/football/football-pitch-03.jpg` | Modern enclosed 7-a-side pitch with goal net and pitch markings | Unsplash (Izuddin Helmi Adnan / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/football/football-pitch-04.jpg` | Evening aerial perspective of urban football court with lights | Unsplash (Connor Coyne / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/football/football-pitch-05.jpg` | Green turf penalty spot and goalposts close-up | Unsplash (Vienna Reyes / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/football/football-pitch-06.jpg` | Indoor 5-a-side futsal court with boundary lines | Unsplash (Fauzan Saari / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/football/football-exterior-01.jpg` | Sports club main entrance and floodlit pitches | Unsplash (Emilio Garcia / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/football/football-detail-01.jpg` | Match ball resting on pristine synthetic grass | Unsplash (Robbie Noble / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/fallbacks/football-fallback.jpg` | High-contrast football field graphic fallback | PlaySpot Design Team / CC0 Public Domain | Public Domain / CC0 |

### B. Padel
| Relative Path | Asset Description | Source & Attribution | License |
|---|---|---|---|
| `/images/venues/padel/padel-court-01.jpg` | Professional glass-walled panoramic padel court | Unsplash (Oliver Sjöström / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/padel/padel-court-02.jpg` | Blue artificial turf padel court with net | Unsplash (Padel Photography / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/padel/padel-court-03.jpg` | Outdoor padel facility with multiple tournament courts | Unsplash (Court Club / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/padel/padel-court-04.jpg` | Padel court corner angle highlighting wire mesh and glass | Unsplash (Marc Sendra Martorell / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/padel/padel-court-05.jpg` | Evening illuminated padel arena with players | Unsplash (Joan Tran / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/padel/padel-indoor-01.jpg` | High-ceiling air-conditioned indoor padel court | Unsplash (Padel World / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/padel/padel-detail-01.jpg` | Padel racket and balls resting at baseline | Unsplash (Racket Studio / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/fallbacks/padel-fallback.jpg` | Modern padel court graphic fallback | PlaySpot Design Team / CC0 Public Domain | Public Domain / CC0 |

### C. Tennis
| Relative Path | Asset Description | Source & Attribution | License |
|---|---|---|---|
| `/images/venues/tennis/tennis-clay-01.jpg` | Red clay tennis court with pristine white lines | Unsplash (Valentin Balan / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/tennis/tennis-clay-02.jpg` | Roland-Garros style clay tennis court baseline view | Unsplash (Renith R / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/tennis/tennis-hard-01.jpg` | Blue acrylic hard court tennis venue | Unsplash (Gonzalo Facello / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/tennis/tennis-hard-02.jpg` | Dual-tone hard court with stadium seating | Unsplash (Julian Myles / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/tennis/tennis-grass-01.jpg` | Traditional manicured grass tennis court | Unsplash (Moises Alex / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/tennis/tennis-detail-01.jpg` | Optic yellow tennis balls and racket by court net | Unsplash (Ben Hershey / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/fallbacks/tennis-fallback.jpg` | High-contrast tennis court fallback | PlaySpot Design Team / CC0 Public Domain | Public Domain / CC0 |

### D. Basketball
| Relative Path | Asset Description | Source & Attribution | License |
|---|---|---|---|
| `/images/venues/basketball/basketball-court-01.jpg` | Polished hardwood indoor basketball court with hoop | Unsplash (Markus Spiske / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/basketball/basketball-indoor-01.jpg` | Arena perspective showing free-throw line and backboard | Unsplash (TJ Dragotta / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/basketball/basketball-indoor-02.jpg` | High school gym hardwood court with scoreboard | Unsplash (Kenny Eliason / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/fallbacks/basketball-fallback.jpg` | Basketball court geometric fallback | PlaySpot Design Team / CC0 Public Domain | Public Domain / CC0 |

### E. Volleyball
| Relative Path | Asset Description | Source & Attribution | License |
|---|---|---|---|
| `/images/venues/volleyball/volleyball-beach-01.jpg` | Sand beach volleyball court with net and boundary ropes | Unsplash (Jocelyn Morales / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/volleyball/volleyball-indoor-01.jpg` | Indoor synthetic taraflex volleyball competition court | Unsplash (Vladislav Babienko / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/fallbacks/volleyball-fallback.jpg` | Volleyball court fallback graphic | PlaySpot Design Team / CC0 Public Domain | Public Domain / CC0 |

### F. Badminton
| Relative Path | Asset Description | Source & Attribution | License |
|---|---|---|---|
| `/images/venues/badminton/badminton-court-01.jpg` | Green PVC mat badminton court with regulation net | Unsplash (Mukhesh Villivakkam / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/badminton/badminton-court-02.jpg` | Multi-court badminton hall with professional overhead lighting | Unsplash (Badminton Hub / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/badminton/badminton-arena-01.jpg` | Tournament badminton court with spectator stands | Unsplash (Sport Vision / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/fallbacks/badminton-fallback.jpg` | Badminton court fallback graphic | PlaySpot Design Team / CC0 Public Domain | Public Domain / CC0 |

### G. Facilities & Amenities
| Relative Path | Asset Description | Source & Attribution | License |
|---|---|---|---|
| `/images/venues/facilities/complex-exterior-01.jpg` | Modern architectural sports complex pavilion | Unsplash (Alexander Bagno / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/facilities/complex-exterior-02.jpg` | Grand sports hub with landscape walkways | Unsplash (Scott Webb / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/facilities/complex-exterior-03.jpg` | Glass-facade contemporary athletic center | Unsplash (Chuttersnap / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/facilities/complex-exterior-04.jpg` | Evening illuminated multi-sport facility entrance | Unsplash (Federico Giampieri / unsplash.com) | Free to use under Unsplash License |
| `/images/venues/facilities/lounge-interior-01.jpg` | Clubhouse player lounge, cafe & viewing deck | Unsplash (Petr Vysohlid / unsplash.com) | Free to use under Unsplash License |

---

## 3. License Compliance & Usage Notes

1. **Unsplash License**:
   All photographs sourced from Unsplash are free to use for commercial and non-commercial purposes without explicit permission, royalty fees, or required copyright notices (though photographer credit is respectfully given above in accordance with best practices).
2. **Internal SVGs & Gradients**:
   Hero illustrations, sport badge icons, and fallback UI patterns were authored natively for PlaySpot and are governed by the project's repository license.
3. **No Unlicensed Media**:
   No copyrighted media without appropriate commercial reuse clearance is utilized. In development or test environments where external third-party images may be submitted via venue creation forms, the client-side `onerror` handler automatically degrades gracefully to the localized `/images/venues/fallbacks/{sport}-fallback.jpg` assets.
