gnuplot << EOF
set terminal wxt enhanced size 1024,768 persist
set grid
set datafile separator ","
set key off
set xlabel "Input"
set ylabel "Output"
set xrange [0:1]
set yrange [0:1.2]
set xtics 0.1
set ytics 0.1
set key on
plot "functions.csv" using 1:3 title "Amplifier(x)" with lines, "functions.csv" using 1:4 title "Predistort(x)" with lines, "functions.csv" using 1:5 title "Amplifier(Predistort(x))" with lines
set terminal pngcairo enhanced size 1024,768
set output "functions.png"
plot "functions.csv" using 1:3 title "Amplifier(x)" with lines, "functions.csv" using 1:4 title "Predistort(x)" with lines, "functions.csv" using 1:5 title "Amplifier(Predistort(x))" with lines
EOF
