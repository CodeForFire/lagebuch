Seven dead local assignments, one of them in `EqualWidthWrapPanel`'s arrange pass, left over
from earlier refactors. `IDE0059` ships at suggestion severity, below the threshold
`TreatWarningsAsErrors` acts on, so they had accumulated unnoticed; it is now a warning and
therefore a build error, so the next one cannot.
